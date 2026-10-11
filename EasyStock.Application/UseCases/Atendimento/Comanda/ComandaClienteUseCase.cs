using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Application.Services.Pedidos;
using EasyStock.Application.Services.Storefront;
using EasyStock.Application.UseCases.Pedidos.Cobranca;
using EasyStock.Application.UseCases.Atendimento.ChatSite;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.Domain.Sales;
using PedidoEntity = EasyStock.Domain.Entities.Pedido;

namespace EasyStock.Application.UseCases.Atendimento.Comanda;

public sealed record RascunhoComandaCliente(IReadOnlyList<ItemPedidoCheckout> Itens, string? Observacoes = null,
    Guid? JanelaId = null, DateOnly? DataEntrega = null, EnderecoCheckout? Endereco = null,
    string Acerto = "estorno", string? CodigoCupom = null, Guid? PedidoId = null, DateTime? PedidoAtualizadoEm = null,
    string? PedidoResumoHash = null);
public sealed record ItemSelecionadoComanda(Guid CardapioItemId, Guid? VariacaoId, string Nome, int Qtd, decimal Preco, string? Observacao);
public sealed record EnvioComandaCliente(Guid PedidoId, string Situacao, decimal Total, decimal Saldo, string? LinkPagamento = null, string? CodigoCupom = null);
public sealed record CupomComandaCliente(string Codigo, decimal Saldo);
public sealed record ComandaClienteResult(Guid LinkId, string Nome, DateOnly Dia, DateTime ExpiraEm, int Revisao, bool PodeEditar,
    RascunhoComandaCliente Rascunho, IReadOnlyList<ItemSelecionadoComanda> Selecao, PedidoConversaResult? Pedido,
    EnvioComandaCliente? Envio, IReadOnlyList<CupomComandaCliente> Cupons, decimal? TotalPrevisto = null,
    long NumeroComanda = 0, Guid ConversaId = default);
public sealed class ComandaClienteConflitoException(string mensagem) : Exception(mensagem);

public sealed class ComandaClienteUseCase(ILinkCardapioConversaRepository links, IConversaRepository conversas,
    IClienteRepository clientes, IPedidoRepository pedidos, IStorefrontRepository vitrines, ICardapioItemRepository pratos, CheckoutCoreService checkout,
    CriarPedidoPeloCardapioConversaUseCase criarPedido, ObterPedidoConversaUseCase obterPedido, SaldoFinanceiroPedido saldoFinanceiro,
    ICreditoClienteComandaRepository creditos, GerarCobrancaPedidoUseCase cobrar, IUnitOfWork uow,
    ITenantContextAccessor tenant, IOperacaoEventPublisher eventos, TimeProvider relogio,
    CalculadoraInicioPrevistoPedido inicioPrevisto, QuitacaoPedido quitacao, LinkCardapioConversaService gerarLink)
{
    public const string Evento = "conversa.comanda_atualizada";
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public static T? Ler<T>(string? json) => json is null ? default : JsonSerializer.Deserialize<T>(json, Json);
    public static bool PodeAlterar(PedidoEntity p) => p.StatusEnum is StatusPedido.AguardandoPagamento or StatusPedido.Aguardando
        or StatusPedido.AguardandoAprovacaoBaba or StatusPedido.AprovadoBaba or StatusPedido.Preparando or StatusPedido.Pronto;

    public async Task<LinkCardapioConversa> AcessoAsync(string token, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 512) throw new LinkCardapioConversaIndisponivelException();
        var link = await links.ObterPorTokenHashAsync(AcessoChatSite.HashDoToken(token.Trim()), ct);
        if (link is null || relogio.GetUtcNow().UtcDateTime >= link.ExpiraEm) throw new LinkCardapioConversaIndisponivelException();
        tenant.SetCurrentTenant(link.EmpresaId);
        var conversa = await conversas.ObterPorIdAsync(link.EmpresaId, link.ConversaId, ct);
        if (conversa is null || !conversa.EstaAberta) throw new LinkCardapioConversaIndisponivelException();
        return link;
    }

    public async Task<ComandaClienteResult> ObterAsync(string token, CancellationToken ct)
    {
        var link = await AcessoAsync(token, ct);
        return await EstadoAsync(link, ct);
    }

    public async Task<LinkCardapioConversaGerado> NovaEdicaoAsync(string token, CancellationToken ct)
    {
        var link = await AcessoAsync(token, ct);
        var estado = await EstadoAsync(link, ct);
        if (link.UsadoEm is null || estado.Pedido is null || estado.Envio?.Situacao is "alteracao_aguardando_confirmacao" or "estorno_pendente")
            throw new ComandaClienteConflitoException("Aguarde a confirmação deste envio antes de abrir outra edição.");
        var pedido = await pedidos.GetByIdWithDetailsAsync(link.EmpresaId, estado.Pedido.PedidoId);
        if (pedido is null || !PodeAlterar(pedido)) throw new LinkCardapioConversaIndisponivelException();
        // Repetir a solicitação após perder a resposta recupera a mesma credencial, sem armazenar token cru.
        var novoToken = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes($"nova-edicao:{link.Id:N}:{token.Trim()}")))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var novo = await gerarLink.GerarAsync(link.EmpresaId, link.ConversaId, relogio.GetUtcNow().UtcDateTime, ct, novoToken);
        await AvisarAsync(link.EmpresaId, link.ConversaId, ct);
        return novo;
    }

    public async Task<ComandaClienteResult> EstadoAsync(LinkCardapioConversa link, CancellationToken ct)
    {
        var conversa = await conversas.ObterPorIdAsync(link.EmpresaId, link.ConversaId, ct)
            ?? throw new LinkCardapioConversaIndisponivelException();
        var cliente = conversa.ClienteId is { } clienteId ? await clientes.GetByIdWithDetailsAsync(link.EmpresaId, clienteId) : null;
        if (cliente is null || cliente.EmpresaId != link.EmpresaId || cliente.Bloqueado)
            throw new UseCaseValidationException("Confirme seu cadastro no atendimento para abrir a comanda.");
        var pedidoId = link.PedidoRegistradoId ?? conversa.PedidoEmAndamentoId;
        var pedido = pedidoId is { } id ? await pedidos.GetByIdWithDetailsAsync(link.EmpresaId, id) : null;
        if (pedido is not null && pedido.ClienteId != cliente.Id) throw new LinkCardapioConversaIndisponivelException();
        // Link novo depois da entrega começa outra comanda. O link usado conserva o acompanhamento.
        if (link.UsadoEm is null && pedido is not null && pedido.EstaFinalizado) pedido = null;
        var dados = Ler<RascunhoComandaCliente>(link.RascunhoJson);
        if (dados?.Itens is null)
        {
            var endereco = cliente.Enderecos.FirstOrDefault(e => e.Id == CriarPedidoAtendimentoUseCase.EnderecoPadrao(cliente));
            dados = new(pedido?.Itens.Where(i => i.CardapioItemId is not null).Select(i =>
                new ItemPedidoCheckout(i.CardapioItemId!.Value, (int)i.Quantidade, i.Observacao, i.CardapioItemVariacaoId)).ToList() ?? [],
                PedidoId: pedido?.Id, PedidoAtualizadoEm: pedido?.AlteradoEm,
                Endereco: endereco is null ? null : new(endereco.Cep ?? "", endereco.Logradouro ?? "", endereco.Numero ?? "",
                    endereco.Bairro ?? "", endereco.Cidade ?? "", endereco.Estado ?? "", endereco.Complemento));
        }
        if (link.UsadoEm is null && pedido is not null && dados.PedidoId == pedido.Id)
            dados = dados with { PedidoAtualizadoEm = pedido.AlteradoEm };
        var selecao = await SelecionarAsync(link.EmpresaId, dados.Itens, pedido, ct, validar: false);
        var detalhe = pedido is null ? null : await obterPedido.ExecuteAsync(link.EmpresaId, conversa.Id, ct, pedido.Id);
        if (detalhe is not null && pedido is not null)
            detalhe = detalhe with { TotalPago = (await saldoFinanceiro.ConsultarAsync(pedido, ct)).Alocado };
        var pode = link.EstaValido(relogio.GetUtcNow().UtcDateTime)
            && conversa.EstaAberta
            && HorarioBrasil.DataOperacional(link.CriadoEm) == HorarioBrasil.DataOperacional(relogio.GetUtcNow().UtcDateTime)
            && (pedido is null || PodeAlterar(pedido));
        return new(link.Id, cliente.Nome.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "",
            HorarioBrasil.DataOperacional(link.CriadoEm), link.ExpiraEm, link.Revisao, pode, dados, selecao, detalhe,
            Ler<EnvioComandaCliente>(link.ResultadoJson), (await creditos.ListarAsync(link.EmpresaId, cliente.Id, ct)).Select(c => new CupomComandaCliente(c.Codigo, c.Saldo)).ToList(),
            selecao.Sum(i => i.Qtd * i.Preco) + (detalhe?.Frete ?? 0m), conversa.NumeroComanda, conversa.Id);
    }

    public async Task<ComandaClienteResult> SalvarAsync(string token, int revisao, RascunhoComandaCliente dados, CancellationToken ct)
    {
        Validar(dados);
        var acesso = await AcessoAsync(token, ct);
        await SalvarLinkAsync(acesso, revisao, dados, ct);
        return await ObterAsync(token, ct);
    }

    public async Task<ComandaClienteResult> SalvarDoAtendimentoAsync(Guid empresaId, Guid conversaId, Guid linkId,
        int revisao, RascunhoComandaCliente dados, CancellationToken ct)
    {
        Validar(dados);
        var acesso = (await links.ListarDaConversaAsync(empresaId, conversaId, relogio.GetUtcNow().UtcDateTime.AddDays(-1), ct))
            .SingleOrDefault(l => l.Id == linkId) ?? throw new LinkCardapioConversaIndisponivelException();
        await SalvarLinkAsync(acesso, revisao, dados, ct);
        var atualizado = (await links.ListarDaConversaAsync(empresaId, conversaId, acesso.CriadoEm, ct)).Single(l => l.Id == linkId);
        return await EstadoAsync(atualizado, ct);
    }

    private async Task SalvarLinkAsync(LinkCardapioConversa acesso, int revisao, RascunhoComandaCliente dados, CancellationToken ct)
    {
        await uow.ExecuteInTransactionSemRetryAsync(async t =>
        {
            await conversas.TravarParaPedidoAsync(acesso.EmpresaId, acesso.ConversaId, t);
            var link = await links.ObterComLockAsync(acesso.EmpresaId, acesso.Id, t) ?? throw new LinkCardapioConversaIndisponivelException();
            var estado = await EstadoAsync(link, t);
            if (!estado.PodeEditar) throw new LinkCardapioConversaIndisponivelException();
            if (link.Revisao != revisao) throw new ComandaClienteConflitoException("A comanda mudou em outra tela. Recarregue para conferir.");
            if (dados.PedidoId != estado.Pedido?.PedidoId) throw new ComandaClienteConflitoException("O pedido mudou. Recarregue a comanda.");
            var pedido = dados.PedidoId is { } id ? await pedidos.GetByIdWithDetailsAsync(link.EmpresaId, id) : null;
            if (pedido is not null && pedido.AlteradoEm != dados.PedidoAtualizadoEm)
                throw new ComandaClienteConflitoException("O atendimento atualizou o pedido. Recarregue antes de alterar.");
            await SelecionarAsync(link.EmpresaId, dados.Itens, pedido, t);
            dados = dados with { PedidoResumoHash = pedido is null ? null : ResumoHash(pedido) };
            link.SalvarRascunho(revisao, JsonSerializer.Serialize(dados, Json), relogio.GetUtcNow().UtcDateTime);
            await uow.CommitAsync();
            return true;
        }, ct);
        await AvisarAsync(acesso.EmpresaId, acesso.ConversaId, ct);
    }

    public async Task<EnvioComandaCliente> EnviarAsync(string token, int revisao, CancellationToken ct)
    {
        var acesso = await AcessoAsync(token, ct);
        EfeitosQuitacao? efeitos = null;
        var resposta = await uow.ExecuteInTransactionSemRetryAsync(async t =>
        {
            await conversas.TravarParaPedidoAsync(acesso.EmpresaId, acesso.ConversaId, t);
            var link = await links.ObterComLockAsync(acesso.EmpresaId, acesso.Id, t) ?? throw new LinkCardapioConversaIndisponivelException();
            if (link.Revisao != revisao) throw new ComandaClienteConflitoException("A seleção mudou. Confira a comanda antes de enviar.");
            if (link.UsadoEm is not null)
                return Ler<EnvioComandaCliente>(link.ResultadoJson) ?? throw new ComandaClienteConflitoException("O envio está em confirmação. Atualize em instantes.");
            var estado = await EstadoAsync(link, t);
            if (!estado.PodeEditar) throw new LinkCardapioConversaIndisponivelException();
            var dados = Ler<RascunhoComandaCliente>(link.RascunhoJson) is { Itens: not null } salvo ? salvo : estado.Rascunho;
            Validar(dados);
            if (dados.Itens.Count == 0) throw new UseCaseValidationException("Escolha ao menos um prato.");
            var agora = relogio.GetUtcNow().UtcDateTime;
            EnvioComandaCliente resultado;
            if (dados.PedidoId is { } pedidoId)
            {
                var pedido = await pedidos.GetByIdWithDetailsAsync(link.EmpresaId, pedidoId)
                    ?? throw new ComandaClienteConflitoException("Pedido não encontrado.");
                if (!PodeAlterar(pedido) || pedido.AlteradoEm != dados.PedidoAtualizadoEm)
                    throw new ComandaClienteConflitoException("O pedido mudou. Recarregue antes de enviar a alteração.");
                var selecao = await SelecionarAsync(link.EmpresaId, dados.Itens, pedido, t);
                var total = selecao.Sum(i => i.Qtd * i.Preco) + estado.Pedido!.Frete;
                link.Consumir(agora);
                resultado = new(pedido.Id, "alteracao_aguardando_confirmacao", total,
                    total - (await saldoFinanceiro.ConsultarAsync(pedido, t)).Alocado);
                await conversas.AddMensagemAsync(Mensagem.Saida(link.EmpresaId, link.ConversaId, AutorMensagem.Sistema, agora,
                    TipoConteudoMensagem.Texto, $"Cliente enviou uma alteração de comanda. Total proposto: {total.ToString("C", Cultura.PtBr)}. Confira a seleção na Ficha."), t);
            }
            else
            {
                if (dados.JanelaId is not { } janela || dados.DataEntrega is not { } dia)
                    throw new UseCaseValidationException("Escolha o horário de entrega antes de enviar.");
                var criado = await criarPedido.ExecuteAsync(new(token, dados.Itens, janela, dia,
                    Observacoes: dados.Observacoes, Endereco: dados.Endereco, SemCobranca: true), t);
                // O núcleo consumiu o link por UPDATE; relê para não sobrescrever o uso no flush.
                link = await links.ObterComLockAsync(acesso.EmpresaId, acesso.Id, t) ?? throw new LinkCardapioConversaIndisponivelException();
                var pedido = await pedidos.GetByIdWithDetailsAsync(link.EmpresaId, criado.PedidoId)
                    ?? throw new InvalidOperationException("Pedido criado sem persistência.");
                await AplicarCupomAsync(pedido, dados.CodigoCupom, t);
                var saldo = pedido.Total.Valor - (await saldoFinanceiro.ConsultarAsync(pedido, t)).Alocado;
                CobrancaPedidoResult? cobranca = null;
                if (saldo > 0)
                    cobranca = await cobrar.ExecuteAsync(new(link.EmpresaId, pedido.Id, link.ConversaId, CobrarSaldo: true), t);
                else
                {
                    pedido.MudarStatus(pedido.RequerAprovacao ? StatusPedido.AguardandoAprovacaoBaba : StatusPedido.Aguardando);
                    await inicioPrevisto.AplicarNaFilaAsync(pedido, t);
                    await pedidos.UpdateAsync(pedido);
                    efeitos = await quitacao.PrepararCreditoAsync(pedido, link.ConversaId, agora, t);
                }
                resultado = new(pedido.Id, saldo > 0 ? "aguardando_pagamento" : "pedido_confirmado", pedido.Total.Valor, saldo, cobranca?.LinkPagamento);
            }
            link.RegistrarResultado(resultado.PedidoId, JsonSerializer.Serialize(resultado, Json));
            await uow.CommitAsync();
            return resultado;
        }, ct);
        await AvisarAsync(acesso.EmpresaId, acesso.ConversaId, ct);
        await quitacao.PublicarAsync(acesso.EmpresaId, efeitos, ct);
        return resposta;
    }

    public async Task AplicarCupomAsync(PedidoEntity pedido, string? codigo, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(codigo)) return;
        if (pedido.ClienteId is not { } clienteId) throw new UseCaseValidationException("Cliente não identificado.");
        var credito = await creditos.ObterComLockAsync(pedido.EmpresaId, clienteId, codigo.Trim().ToUpperInvariant(), ct)
            ?? throw new UseCaseValidationException("Este cupom não está disponível para seu cadastro.");
        var saldo = await saldoFinanceiro.ConsultarAsync(pedido, ct);
        var valor = Math.Min(credito.Saldo, Math.Max(0, pedido.Total.Valor - saldo.Alocado));
        if (valor <= 0) throw new UseCaseValidationException("Não há saldo a pagar com esse cupom.");
        var uso = credito.Usar(pedido.Id, valor, relogio.GetUtcNow().UtcDateTime);
        await creditos.AdicionarUsoAsync(uso, ct);
        await pedidos.AddEventoAsync(new() { Id = Guid.NewGuid(), PedidoId = pedido.Id, Tipo = "cupom_cliente_aplicado",
            Origem = "comanda_cliente", OcorridoEm = relogio.GetUtcNow().UtcDateTime, Detalhes = $"{credito.Codigo}: {valor}" });
        await uow.CommitAsync();
    }

    public async Task<IReadOnlyList<ItemSelecionadoComanda>> SelecionarAsync(Guid empresaId, IReadOnlyList<ItemPedidoCheckout> itens,
        PedidoEntity? pedido, CancellationToken ct, bool validar = true)
    {
        if (itens.Count == 0) return [];
        var loja = await vitrines.GetByEmpresaAsync(empresaId, ct) ?? throw new UseCaseValidationException("Cardápio indisponível.");
        var menu = validar ? await checkout.CarregarItensCardapioAsync(loja.Id, itens.Select(i => i.CardapioItemId), ct)
            : (await pratos.GetTodosDoStorefrontAsync(loja.Id, ct)).ToDictionary(p => p.Id);
        return itens.Select(i =>
        {
            if (!menu.TryGetValue(i.CardapioItemId, out var prato))
            {
                var salvo = pedido?.Itens.FirstOrDefault(p => p.CardapioItemId == i.CardapioItemId && p.CardapioItemVariacaoId == i.VariacaoId);
                return new ItemSelecionadoComanda(i.CardapioItemId, i.VariacaoId, salvo?.Nome ?? "Item indisponível", i.Qtd, salvo?.PrecoUnitario ?? 0m, i.Observacao);
            }
            var porcao = validar ? prato.PorcaoParaVenda(i.VariacaoId)
                : prato.Variacoes.FirstOrDefault(v => i.VariacaoId is null ? v.EhPadrao : v.Id == i.VariacaoId);
            var anterior = pedido?.Itens.FirstOrDefault(p => p.CardapioItemId == i.CardapioItemId && p.CardapioItemVariacaoId == porcao?.Id);
            return new ItemSelecionadoComanda(i.CardapioItemId, porcao?.Id, prato.NomeEfetivo() ?? "", i.Qtd,
                anterior?.PrecoUnitario ?? porcao?.PrecoStorefront ?? prato.PrecoEfetivo(), i.Observacao?.Trim());
        }).ToList();
    }

    public Task AvisarAsync(Guid empresaId, Guid conversaId, CancellationToken ct) =>
        eventos.PublicarAsync(Evento, empresaId, new { conversaId }, ct);

    public static void Validar(RascunhoComandaCliente dados)
    {
        if (dados?.Itens is null || dados.Itens.Count > 50 || dados.Itens.Any(i => i.CardapioItemId == Guid.Empty || i.Qtd is < 1 or > 99 || i.Observacao?.Length > 500)
            || dados.Itens.GroupBy(i => (i.CardapioItemId, i.VariacaoId)).Any(g => g.Count() > 1)
            || dados.Observacoes?.Length > 2000 || dados.Acerto is not ("estorno" or "cupom") || dados.CodigoCupom?.Length > 35)
            throw new UseCaseValidationException("Confira os itens, quantidades e observações da comanda.");
    }

    public static string ResumoHash(PedidoEntity pedido) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
    {
        pedido.Observacoes, pedido.AgendadoParaEm,
        Itens = pedido.Itens.OrderBy(i => i.Id).Select(i => new { i.Id, i.CardapioItemId, i.CardapioItemVariacaoId, i.Quantidade, i.PrecoUnitario, i.Observacao })
    }, Json)));
}
