using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Ports.Output.Persistence.Pagamentos;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Application.Services.Pedidos;
using EasyStock.Application.Services.Storefront;
using EasyStock.Application.UseCases.Pedidos.Cobranca;
using EasyStock.Application.UseCases.Atendimento.ChatSite;
using EasyStock.Application.UseCases.Atendimento.ClienteDaConversa;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Entities.Pagamentos;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.Domain.Enums.Pagamentos;
using EasyStock.Domain.Sales;
using PedidoEntity = EasyStock.Domain.Entities.Pedido;

namespace EasyStock.Application.UseCases.Atendimento.Comanda;

public sealed record RascunhoComandaCliente(IReadOnlyList<ItemPedidoCheckout> Itens, string? Observacoes = null,
    Guid? JanelaId = null, DateOnly? DataEntrega = null, EnderecoCheckout? Endereco = null,
    string Acerto = "estorno", string? CodigoCupom = null, Guid? PedidoId = null, DateTime? PedidoAtualizadoEm = null,
    string? PedidoResumoHash = null, string? Nome = null, string? Telefone = null);
public sealed record ItemSelecionadoComanda(Guid CardapioItemId, Guid? VariacaoId, string Nome, int Qtd, decimal Preco, string? Observacao);
public sealed record CupomAplicadoComandaCliente(Guid UsoId, string Codigo, decimal Valor, decimal SaldoCupom);
public sealed record EnvioComandaCliente(Guid PedidoId, string Situacao, decimal Total, decimal Saldo, string? LinkPagamento = null,
    string? CodigoCupom = null, CupomAplicadoComandaCliente? CupomAplicado = null);
public sealed record CupomComandaCliente(string Codigo, decimal Saldo, decimal? ValorEmitido = null, DateTime? CriadoEm = null);
public sealed record DevolucaoComandaCliente(Guid OperacaoId, decimal Valor, string Situacao, string? Meio, string Provedor,
    DateTime CriadoEm, DateTime? ConfirmadoEm, string? Referencia);
public sealed record AcertoComandaCliente(string Tipo, decimal Valor, string Situacao, IReadOnlyList<DevolucaoComandaCliente> Devolucoes,
    string? CodigoCupom = null, DateTime? ConfirmadoEm = null, decimal? SaldoCupom = null);
public sealed record ComandaClienteResult(Guid LinkId, string Nome, DateOnly Dia, DateTime ExpiraEm, int Revisao, bool PodeEditar,
    RascunhoComandaCliente Rascunho, IReadOnlyList<ItemSelecionadoComanda> Selecao, PedidoConversaResult? Pedido,
    EnvioComandaCliente? Envio, IReadOnlyList<CupomComandaCliente> Cupons, decimal? TotalPrevisto = null,
    long NumeroComanda = 0, Guid ConversaId = default, SaldoFinanceiroComanda? Financeiro = null, AcertoComandaCliente? Acerto = null,
    bool PrecisaCadastro = false, bool EntregaACombinar = false, decimal? FreteCombinado = null,
    bool AjudaSolicitada = false, DateTime? AjudaSolicitadaEm = null);
public sealed record FreteCombinadoComanda(Guid LinkId, int Revisao, decimal Valor);
public sealed record EntregaACombinarComanda(Guid LinkId, int Revisao);
public sealed class ComandaClienteConflitoException(string mensagem) : Exception(mensagem);

public sealed class ComandaClienteUseCase(ILinkCardapioConversaRepository links, IConversaRepository conversas,
    IClienteRepository clientes, IPedidoRepository pedidos, IStorefrontRepository vitrines, ICardapioItemRepository pratos, CheckoutCoreService checkout,
    CriarPedidoPeloCardapioConversaUseCase criarPedido, ObterPedidoConversaUseCase obterPedido, SaldoFinanceiroPedido saldoFinanceiro,
    ICreditoClienteComandaRepository creditos, GerarCobrancaPedidoUseCase cobrar, IUnitOfWork uow,
    ITenantContextAccessor tenant, IOperacaoEventPublisher eventos, TimeProvider relogio,
    CalculadoraInicioPrevistoPedido inicioPrevisto, QuitacaoPedido quitacao, LinkCardapioConversaService gerarLink,
    IEstornoOnlineRepository estornos, AvisoStatusPedidoCliente avisoCliente, CadastrarClienteDaConversaUseCase cadastrarCliente)
{
    private const string RevisaoEntregaACombinar = "comandaEntregaACombinarRevisao";
    private const string FreteCombinadoChave = "comandaFreteCombinado";
    private const string AjudaSolicitadaChave = Conversa.ChaveAjudaComandaSolicitadaEm;
    public const string Evento = "conversa.comanda_atualizada";
    public const string PrefixoAvisoChatSite = CanalChatSite.PrefixoIdExterno + "comanda:";
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
        var acesso = await AcessoAsync(token, ct);
        var novo = await uow.ExecuteInTransactionSemRetryAsync(async t =>
        {
            await conversas.TravarParaPedidoAsync(acesso.EmpresaId, acesso.ConversaId, t);
            var link = await AcessoAsync(token, t);
            var estado = await EstadoAsync(link, t);
            if (link.UsadoEm is null || estado.Pedido is null || estado.Envio?.Situacao is "alteracao_aguardando_confirmacao" or "estorno_pendente")
                throw new ComandaClienteConflitoException("Aguarde a confirmação deste envio antes de abrir outra edição.");
            var pedido = await pedidos.GetByIdWithDetailsAsync(link.EmpresaId, estado.Pedido.PedidoId);
            if (pedido is null || !PodeAlterar(pedido)) throw new LinkCardapioConversaIndisponivelException();
            var anterior = link;
            var tokenAnterior = token.Trim();
            var agora = relogio.GetUtcNow().UtcDateTime;
            while (true)
            {
                // Recibos anteriores convergem à próxima edição, mesmo após o consumo de seus filhos.
                var novoToken = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes($"nova-edicao:{anterior.Id:N}:{tokenAnterior}")))
                    .TrimEnd('=').Replace('+', '-').Replace('/', '_');
                var filho = await links.ObterPorTokenHashAsync(AcessoChatSite.HashDoToken(novoToken), t);
                if (filho is not null && (filho.EmpresaId != link.EmpresaId || filho.ConversaId != link.ConversaId))
                    throw new LinkCardapioConversaIndisponivelException();
                if (filho is null || filho.EstaValido(agora))
                    return await gerarLink.GerarAsync(link.EmpresaId, link.ConversaId, agora, t, novoToken);
                anterior = filho;
                tokenAnterior = novoToken;
            }
        }, ct);
        await AvisarAsync(acesso.EmpresaId, acesso.ConversaId, ct);
        return novo;
    }

    public async Task<ComandaClienteResult> EstadoAsync(LinkCardapioConversa link, CancellationToken ct)
    {
        var conversa = await conversas.ObterPorIdAsync(link.EmpresaId, link.ConversaId, ct)
            ?? throw new LinkCardapioConversaIndisponivelException();
        var cliente = conversa.ClienteId is { } clienteId ? await clientes.GetByIdWithDetailsAsync(link.EmpresaId, clienteId) : null;
        if (conversa.ClienteId is not null && (cliente is null || cliente.EmpresaId != link.EmpresaId || cliente.Bloqueado))
            throw new UseCaseValidationException("Confirme seu cadastro no atendimento para abrir a comanda.");
        var pedidoId = link.PedidoRegistradoId ?? conversa.PedidoEmAndamentoId;
        var pedido = pedidoId is { } id ? await pedidos.GetByIdWithDetailsAsync(link.EmpresaId, id) : null;
        // Pedido do site ligado ao chat sem conceder identidade (#1865): de outro cadastro, o link começa comanda nova
        // e não expõe o pedido. Só o pedido registrado pelo próprio link exige o mesmo cliente.
        if (pedido is not null && link.PedidoRegistradoId is null && pedido.ClienteId != cliente?.Id) pedido = null;
        if (pedido is not null && pedido.ClienteId != cliente?.Id) throw new LinkCardapioConversaIndisponivelException();
        // Link novo depois da entrega começa outra comanda. O link usado conserva o acompanhamento.
        if (link.UsadoEm is null && pedido is not null && pedido.EstaFinalizado) pedido = null;
        var dados = Ler<RascunhoComandaCliente>(link.RascunhoJson);
        if (dados?.Itens is null)
        {
            var endereco = cliente?.Enderecos.FirstOrDefault(e => e.Id == CriarPedidoAtendimentoUseCase.EnderecoPadrao(cliente));
            dados = new(pedido?.Itens.Where(i => i.CardapioItemId is not null).Select(i =>
                new ItemPedidoCheckout(i.CardapioItemId!.Value, (int)i.Quantidade, i.Observacao, i.CardapioItemVariacaoId)).ToList() ?? [],
                Observacoes: pedido?.Observacoes, PedidoId: pedido?.Id, PedidoAtualizadoEm: pedido?.AlteradoEm,
                Endereco: endereco is null ? null : new(endereco.Cep ?? "", endereco.Logradouro ?? "", endereco.Numero ?? "",
                    endereco.Bairro ?? "", endereco.Cidade ?? "", endereco.Estado ?? "", endereco.Complemento));
        }
        // Endereço em branco é endereço não informado (#1839): a cliente finaliza sem ele e a atendente registra pela Ficha.
        dados = dados with { Endereco = Informado(dados.Endereco) };
        // Comanda entregue à atendente com endereço incompleto: vale o que ela registrar na Ficha (#1839).
        var comAtendente = ContextoConversaJson.Ler<EntregaACombinarComanda>(conversa, RevisaoEntregaACombinar)?.LinkId == link.Id
            || ContextoConversaJson.Ler<FreteCombinadoComanda>(conversa, FreteCombinadoChave)?.LinkId == link.Id;
        // Rascunhos anteriores podem ter itens e ainda não ter recebido o endereço do cadastro.
        if ((dados.Endereco is null || comAtendente && Valido(dados.Endereco) is null) && cliente is not null)
        {
            var endereco = cliente.Enderecos.FirstOrDefault(e => e.Id == CriarPedidoAtendimentoUseCase.EnderecoPadrao(cliente));
            if (endereco is not null) dados = dados with { Endereco = new(endereco.Cep ?? "", endereco.Logradouro ?? "",
                endereco.Numero ?? "", endereco.Bairro ?? "", endereco.Cidade ?? "", endereco.Estado ?? "", endereco.Complemento) };
        }
        if (link.UsadoEm is null && pedido is not null && dados.PedidoId == pedido.Id)
            dados = dados with { PedidoAtualizadoEm = pedido.AlteradoEm };
        var selecao = await SelecionarAsync(link.EmpresaId, dados.Itens, pedido, ct, validar: false);
        var detalhe = pedido is null ? null : await obterPedido.ExecuteAsync(link.EmpresaId, conversa.Id, ct, pedido.Id);
        var financeiro = pedido is null ? null : await saldoFinanceiro.ConsultarAsync(pedido, ct);
        if (detalhe is not null && financeiro is not null) detalhe = detalhe with { TotalPago = financeiro.Alocado };
        var acerto = pedido is null ? null : await AcertoAsync(link, pedido, ct);
        var envio = Ler<EnvioComandaCliente>(link.ResultadoJson);
        if (envio?.CupomAplicado is { } usado)
        {
            var cupom = cliente is null ? null : await creditos.ObterPorCodigoAsync(link.EmpresaId, cliente.Id, usado.Codigo, ct);
            if (cupom is not null) envio = envio with { CupomAplicado = usado with { SaldoCupom = cupom.Saldo } };
        }
        if (envio is not null && acerto?.Tipo == "estorno")
            envio = envio with { Situacao = acerto.Situacao switch
            {
                "confirmado" => "estorno_confirmado", "recusado" => "estorno_recusado", _ => "estorno_pendente"
            } };
        var pode = link.EstaValido(relogio.GetUtcNow().UtcDateTime)
            && conversa.EstaAberta
            && HorarioBrasil.DataOperacional(link.CriadoEm) == HorarioBrasil.DataOperacional(relogio.GetUtcNow().UtcDateTime)
            && (pedido is null || PodeAlterar(pedido));
        var freteCombinado = ContextoConversaJson.Ler<FreteCombinadoComanda>(conversa, FreteCombinadoChave);
        var valorCombinado = freteCombinado?.LinkId == link.Id && freteCombinado.Revisao == link.Revisao ? freteCombinado.Valor : (decimal?)null;
        var ajudaEm = ContextoConversaJson.Ler<DateTime?>(conversa, AjudaSolicitadaChave);
        return new(link.Id, (cliente?.Nome ?? conversa.ContatoNome ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "",
            HorarioBrasil.DataOperacional(link.CriadoEm), link.ExpiraEm, link.Revisao, pode, dados, selecao, detalhe,
            envio, cliente is null ? [] : (await creditos.ListarAsync(link.EmpresaId, cliente.Id, ct)).Select(c => new CupomComandaCliente(c.Codigo, c.Saldo, c.Valor, c.CriadoEm)).ToList(),
            selecao.Sum(i => i.Qtd * i.Preco) + (detalhe?.Frete ?? valorCombinado ?? 0m), conversa.NumeroComanda, conversa.Id, financeiro, acerto,
            cliente is null, valorCombinado is null && ContextoConversaJson.Ler<EntregaACombinarComanda>(conversa, RevisaoEntregaACombinar) == new EntregaACombinarComanda(link.Id, link.Revisao), valorCombinado,
            ajudaEm is not null && conversa.Situacao == SituacaoConversa.Assumida, ajudaEm);
    }

    private async Task<AcertoComandaCliente?> AcertoAsync(LinkCardapioConversa link, PedidoEntity pedido, CancellationToken ct)
    {
        var credito = await creditos.ObterPorOperacaoAsync(link.EmpresaId, link.Id, ct);
        if (credito is not null && credito.PedidoOrigemId == pedido.Id)
            return new("cupom", credito.Valor, "confirmado", [], credito.Codigo, credito.CriadoEm, credito.Saldo);
        var proprios = (await estornos.ListarAsync(link.EmpresaId, pedido.Id, ct))
            .Where(e => e.Id == ConfirmarAlteracaoComandaUseCase.IdEstorno(link.Id, e.PagamentoId)).ToList();
        if (proprios.Count == 0) return null;
        var pagamentos = await estornos.RecebimentosAsync(link.EmpresaId, pedido.Id, ct);
        var situacao = proprios.Any(e => e.Situacao == PedidoEstornoOnline.Pendente) ? "pendente"
            : proprios.All(e => e.Situacao == PedidoEstornoOnline.Confirmado) ? "confirmado" : "recusado";
        return new("estorno", proprios.Sum(e => e.Valor), situacao, proprios.Select(e => new DevolucaoComandaCliente(
            e.Id, e.Valor, e.Situacao, pagamentos.SingleOrDefault(p => p.PagamentoId == e.PagamentoId)?.Metodo,
            "mercadopago", e.CriadoEm, e.ConfirmadoEm, e.EstornoExternoId)).ToList(),
            ConfirmadoEm: situacao == "confirmado" ? proprios.Max(e => e.ConfirmadoEm) : null);
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
        dados = dados with { Endereco = Informado(dados.Endereco) };
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
            // Data e horário não alteram a cotação manual (#1717-6); qualquer outra edição continua exigindo novo acordo.
            if (estado.Pedido is null && estado.FreteCombinado is decimal frete && dados.DataEntrega is not null &&
                dados.JanelaId is not null && estado.Rascunho.JanelaId is not null &&
                (dados.JanelaId != estado.Rascunho.JanelaId || dados.DataEntrega != estado.Rascunho.DataEntrega) &&
                dados.Itens.SequenceEqual(estado.Rascunho.Itens) &&
                (dados with { JanelaId = estado.Rascunho.JanelaId, DataEntrega = estado.Rascunho.DataEntrega, Itens = estado.Rascunho.Itens }) == estado.Rascunho)
            {
                var conversa = await conversas.ObterPorIdAsync(acesso.EmpresaId, acesso.ConversaId, t)
                    ?? throw new LinkCardapioConversaIndisponivelException();
                ContextoConversaJson.Gravar(conversa, FreteCombinadoChave, new FreteCombinadoComanda(link.Id, link.Revisao, frete));
            }
            await uow.CommitAsync();
            return true;
        }, ct);
        await AvisarAsync(acesso.EmpresaId, acesso.ConversaId, ct);
    }

    public async Task<EnvioComandaCliente> EnviarAsync(string token, int revisao, CancellationToken ct)
    {
        var acesso = await AcessoAsync(token, ct);
        return await EnviarAsync(acesso, revisao, ct);
    }

    public async Task<EnvioComandaCliente> ConfirmarPedidoDoAtendimentoAsync(Guid empresaId, Guid conversaId, Guid linkId,
        int revisao, CancellationToken ct)
    {
        var acesso = (await links.ListarDaConversaAsync(empresaId, conversaId, relogio.GetUtcNow().UtcDateTime.AddDays(-1), ct))
            .SingleOrDefault(l => l.Id == linkId) ?? throw new LinkCardapioConversaIndisponivelException();
        return await EnviarAsync(acesso, revisao, ct, peloAtendimento: true);
    }

    private async Task<EnvioComandaCliente> EnviarAsync(LinkCardapioConversa acesso, int revisao, CancellationToken ct,
        bool peloAtendimento = false)
    {
        await ConfirmarCadastroAsync(acesso, revisao, ct);
        EfeitosQuitacao? efeitos = null;
        var resposta = await uow.ExecuteInTransactionSemRetryAsync(async t =>
        {
            await conversas.TravarParaPedidoAsync(acesso.EmpresaId, acesso.ConversaId, t);
            var link = await links.ObterComLockAsync(acesso.EmpresaId, acesso.Id, t) ?? throw new LinkCardapioConversaIndisponivelException();
            if (link.Revisao != revisao) throw new ComandaClienteConflitoException("A seleção mudou. Confira a comanda antes de enviar.");
            if (peloAtendimento && Ler<RascunhoComandaCliente>(link.RascunhoJson)?.PedidoId is not null)
                throw new ComandaClienteConflitoException("Esta seleção já é um ajuste. Confira a alteração no pedido existente.");
            if (link.UsadoEm is not null)
                return Ler<EnvioComandaCliente>(link.ResultadoJson) ?? throw new ComandaClienteConflitoException("O envio está em confirmação. Atualize em instantes.");
            var estado = await EstadoAsync(link, t);
            if (!estado.PodeEditar) throw new LinkCardapioConversaIndisponivelException();
            if (peloAtendimento && estado.Pedido is not null)
                throw new ComandaClienteConflitoException("A conversa já tem um pedido. Atualize antes de continuar.");
            var dados = Ler<RascunhoComandaCliente>(link.RascunhoJson) is { Itens: not null } salvo ? salvo : estado.Rascunho;
            // Endereço parcial no rascunho cede só a um substituto válido do estado (o da Ficha, com a atendente);
            // sem ele, o parcial fica e a validação do pedido recusa, sem cair no endereço do cadastro.
            if (Valido(dados.Endereco) is null && Valido(estado.Rascunho.Endereco) is { } substituto) dados = dados with { Endereco = substituto };
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
                if (peloAtendimento && estado.FreteCombinado is null)
                    throw new UseCaseValidationException("Combine o frete com o cliente antes de confirmar o pedido pelo atendimento.");
                var criado = await criarPedido.ExecuteComLinkAsync(new(null, dados.Itens, janela, dia,
                    Observacoes: dados.Observacoes, Endereco: dados.Endereco, SemCobranca: true), link, t,
                    freteCombinado: estado.FreteCombinado, peloAtendimento: peloAtendimento);
                // O núcleo consumiu o link por UPDATE; relê para não sobrescrever o uso no flush.
                link = await links.ObterComLockAsync(acesso.EmpresaId, acesso.Id, t) ?? throw new LinkCardapioConversaIndisponivelException();
                var pedido = await pedidos.GetByIdWithDetailsAsync(link.EmpresaId, criado.PedidoId)
                    ?? throw new InvalidOperationException("Pedido criado sem persistência.");
                var cupomAplicado = await AplicarCupomAsync(pedido, dados.CodigoCupom, t);
                var saldo = pedido.Total.Valor - (await saldoFinanceiro.ConsultarAsync(pedido, t)).Alocado;
                CobrancaPedidoResult? cobranca = null;
                // #1726: confirmado pela atendente também nasce com cobrança; sem ela a varredura de abandonados
                // cancelava o pedido em silêncio 30 minutos depois.
                if (saldo > 0)
                    cobranca = await cobrar.ExecuteAsync(new(link.EmpresaId, pedido.Id, link.ConversaId, CobrarSaldo: true,
                        NaEntregaSemProvedor: peloAtendimento), t);
                else if (saldo <= 0)
                {
                    pedido.MudarStatus(pedido.RequerAprovacao ? StatusPedido.AguardandoAprovacaoBaba : StatusPedido.Aguardando);
                    await inicioPrevisto.AplicarNaFilaAsync(pedido, t);
                    await pedidos.UpdateAsync(pedido);
                    efeitos = await quitacao.PrepararCreditoAsync(pedido, link.ConversaId, agora, t);
                }
                resultado = new(pedido.Id, saldo > 0 ? "aguardando_pagamento" : "pedido_confirmado", pedido.Total.Valor, saldo,
                    cobranca?.LinkPagamento, CupomAplicado: cupomAplicado);
                await AvisarChatSiteAsync(link, $"pedido:{pedido.Id:N}", TextoPedidoChatSite(pedido, resultado, cobranca, agora), agora, t);
            }
            link.RegistrarResultado(resultado.PedidoId, JsonSerializer.Serialize(resultado, Json));
            await uow.CommitAsync();
            return resultado;
        }, ct);
        await AvisarAsync(acesso.EmpresaId, acesso.ConversaId, ct);
        await quitacao.PublicarAsync(acesso.EmpresaId, efeitos, ct);
        return resposta;
    }

    /// <param name="exigirEndereco">Falso só na entrega a combinar (#1839): a atendente pede o endereço na conversa.</param>
    private Task<bool> ConfirmarCadastroAsync(LinkCardapioConversa acesso, int revisao, CancellationToken ct, bool exigirEndereco = true) =>
        uow.ExecuteInTransactionSemRetryAsync(async t =>
        {
            await conversas.ObterPorIdComLockAsync(acesso.EmpresaId, acesso.ConversaId, t);
            var link = await links.ObterComLockAsync(acesso.EmpresaId, acesso.Id, t) ?? throw new LinkCardapioConversaIndisponivelException();
            if (link.Revisao != revisao) throw new ComandaClienteConflitoException("A seleção mudou. Confira antes de enviar.");
            var estado = await EstadoAsync(link, t);
            if (!estado.PrecisaCadastro) return true;
            if (!estado.PodeEditar) throw new LinkCardapioConversaIndisponivelException();
            var dados = estado.Rascunho;
            Validar(dados);
            if (dados.Itens.Count == 0) throw new UseCaseValidationException("Escolha ao menos um prato.");
            if (string.IsNullOrWhiteSpace(dados.Nome) || string.IsNullOrWhiteSpace(dados.Telefone))
                throw new UseCaseValidationException("Informe seu nome e telefone para a Casa acompanhar seu pedido.");
            var endereco = exigirEndereco ? dados.Endereco?.Validar() : Valido(dados.Endereco);
            if (endereco is null && exigirEndereco) throw new UseCaseValidationException("Informe seu endereço de entrega.");
            await cadastrarCliente.ExecuteAsync(new(acesso.EmpresaId, acesso.ConversaId, dados.Nome, dados.Telefone,
                endereco is null ? null : new(endereco.Cep, endereco.Logradouro, endereco.Numero, endereco.Complemento, endereco.Bairro, endereco.Cidade, endereco.Uf),
                ReutilizarCadastro: false), t);
            return true;
        }, ct);

    public async Task<ComandaClienteResult> CombinarFreteAsync(Guid empresaId, Guid conversaId, Guid linkId, int revisao,
        decimal valor, string quem, CancellationToken ct)
    {
        if (valor < 0 || valor > 100000 || decimal.Round(valor, 2) != valor)
            throw new UseCaseValidationException("Informe um frete válido em reais e centavos.");
        var link = await uow.ExecuteInTransactionSemRetryAsync(async t =>
        {
            await conversas.TravarParaPedidoAsync(empresaId, conversaId, t);
            var acesso = await links.ObterComLockAsync(empresaId, linkId, t) ?? throw new LinkCardapioConversaIndisponivelException();
            if (acesso.ConversaId != conversaId) throw new LinkCardapioConversaIndisponivelException();
            if (acesso.Revisao != revisao) throw new ComandaClienteConflitoException("A seleção mudou. Confira o endereço antes de combinar o frete.");
            var estado = await EstadoAsync(acesso, t);
            if (estado.FreteCombinado == valor && estado.Pedido is null) return acesso;
            if (!estado.PodeEditar || estado.Pedido is not null || !estado.EntregaACombinar)
                throw new UseCaseValidationException("Esta comanda não tem entrega pendente para combinar.");
            var conversa = await conversas.ObterPorIdAsync(empresaId, conversaId, t) ?? throw new LinkCardapioConversaIndisponivelException();
            ContextoConversaJson.Gravar(conversa, FreteCombinadoChave, new FreteCombinadoComanda(linkId, revisao, valor));
            await conversas.AddMensagemAsync(Mensagem.Saida(empresaId, conversaId, AutorMensagem.Sistema,
                relogio.GetUtcNow().UtcDateTime, TipoConteudoMensagem.Texto,
                $"Frete de {valor.ToString("C", Cultura.PtBr)} combinado por {quem}. O cliente precisa conferir e confirmar o total na comanda."), t);
            await AvisarChatSiteAsync(acesso, $"frete:{acesso.Id:N}:{revisao}",
                $"O frete combinado é {valor.ToString("C", Cultura.PtBr)}. Confira e confirme o total na comanda para enviar o pedido.",
                relogio.GetUtcNow().UtcDateTime, t);
            await uow.CommitAsync();
            return acesso;
        }, ct);
        await AvisarAsync(empresaId, conversaId, ct);
        return await EstadoAsync(link, ct);
    }

    public async Task<ComandaClienteResult> EncaminharEntregaAsync(string token, int revisao, CancellationToken ct)
    {
        var acesso = await AcessoAsync(token, ct);
        return await EncaminharEntregaAsync(acesso, revisao, ct);
    }

    public async Task<ComandaClienteResult> EncaminharEntregaDoAtendimentoAsync(Guid empresaId, Guid conversaId, Guid linkId,
        int revisao, CancellationToken ct)
    {
        var acesso = (await links.ListarDaConversaAsync(empresaId, conversaId, relogio.GetUtcNow().UtcDateTime.AddDays(-1), ct))
            .SingleOrDefault(l => l.Id == linkId) ?? throw new LinkCardapioConversaIndisponivelException();
        return await EncaminharEntregaAsync(acesso, revisao, ct);
    }

    private async Task<ComandaClienteResult> EncaminharEntregaAsync(LinkCardapioConversa acesso, int revisao, CancellationToken ct)
    {
        await ConfirmarCadastroAsync(acesso, revisao, ct, exigirEndereco: false);
        await uow.ExecuteInTransactionSemRetryAsync(async t =>
        {
            var conversa = await conversas.ObterPorIdComLockAsync(acesso.EmpresaId, acesso.ConversaId, t)
                ?? throw new LinkCardapioConversaIndisponivelException();
            var link = await links.ObterComLockAsync(acesso.EmpresaId, acesso.Id, t) ?? throw new LinkCardapioConversaIndisponivelException();
            if (link.Revisao != revisao) throw new ComandaClienteConflitoException("A seleção mudou. Confira antes de enviar.");
            var estado = await EstadoAsync(link, t);
            if (!estado.PodeEditar || estado.Pedido is not null) throw new LinkCardapioConversaIndisponivelException();
            Validar(estado.Rascunho);
            if (estado.Rascunho.Itens.Count == 0) throw new UseCaseValidationException("Escolha ao menos um prato.");
            var endereco = Valido(estado.Rascunho.Endereco);
            if (endereco is not null)
                await cadastrarCliente.ExecuteAsync(new(acesso.EmpresaId, acesso.ConversaId, null, null,
                    new(endereco.Cep, endereco.Logradouro, endereco.Numero, endereco.Complemento, endereco.Bairro, endereco.Cidade, endereco.Uf)), t);
            if (ContextoConversaJson.Ler<EntregaACombinarComanda>(conversa, RevisaoEntregaACombinar) == new EntregaACombinarComanda(link.Id, revisao)) return true;
            var agora = relogio.GetUtcNow().UtcDateTime;
            conversa.Escalar(agora, endereco is null ? "Endereço pendente. Peça o endereço ao cliente e combine a entrega e o frete."
                : "Entrega e frete a combinar. Confira o endereço e a seleção da comanda.");
            ContextoConversaJson.Gravar(conversa, RevisaoEntregaACombinar, new EntregaACombinarComanda(link.Id, revisao));
            await conversas.AddMensagemAsync(Mensagem.Saida(acesso.EmpresaId, acesso.ConversaId, AutorMensagem.Sistema,
                agora, TipoConteudoMensagem.Texto, endereco is null
                    ? "Cliente enviou a comanda sem endereço completo. Endereço pendente: peça o endereço na conversa e registre na Ficha. " +
                      "Seleção preservada. Frete pendente de decisão humana; não cobrar nem prometer entrega ainda."
                    : "Cliente enviou a comanda para combinar entrega fora da área automática. " +
                      "Seleção e endereço preservados. Frete pendente de decisão humana; não cobrar nem prometer entrega ainda."), t);
            await uow.CommitAsync();
            return true;
        }, ct);
        await AvisarAsync(acesso.EmpresaId, acesso.ConversaId, ct);
        return await EstadoAsync(acesso, ct);
    }

    public async Task<ComandaClienteResult> PedirAjudaAsync(string token, CancellationToken ct)
    {
        var acesso = await AcessoAsync(token, ct);
        await uow.ExecuteInTransactionSemRetryAsync(async t =>
        {
            // Relê após o lock: duas abas podem pedir ajuda sem alterar a revisão do rascunho.
            var conversa = await conversas.ObterPorIdComLockAsync(acesso.EmpresaId, acesso.ConversaId, t)
                ?? throw new LinkCardapioConversaIndisponivelException();
            await AcessoAsync(token, t);
            if (conversa.Situacao == SituacaoConversa.Assumida
                && ContextoConversaJson.Ler<DateTime?>(conversa, AjudaSolicitadaChave) is not null) return true;
            var agora = relogio.GetUtcNow().UtcDateTime;
            conversa.Escalar(agora, "Cliente pediu ajuda pela comanda. Confira a conversa e a seleção antes de continuar.");
            ContextoConversaJson.Gravar(conversa, AjudaSolicitadaChave, agora);
            await conversas.AddMensagemAsync(Mensagem.Saida(acesso.EmpresaId, acesso.ConversaId, AutorMensagem.Sistema,
                agora, TipoConteudoMensagem.Texto, "Cliente pediu ajuda pela comanda. A solicitação chegou ao atendimento; seleção preservada, sem confirmar pedido ou pagamento."), t);
            await uow.CommitAsync();
            return true;
        }, ct);
        await AvisarAsync(acesso.EmpresaId, acesso.ConversaId, ct);
        return await ObterAsync(token, ct);
    }

    public async Task<CupomAplicadoComandaCliente?> AplicarCupomAsync(PedidoEntity pedido, string? codigo, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(codigo)) return null;
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
        await avisoCliente.EnfileirarAsync(Domain.Enums.Notifications.TipoEventoNotificacao.ComandaAtualizada,
            pedido.EmpresaId, pedido.Id, $"cupom-usado:{uso.Id:N}", ct,
            mensagem: $"Cupom {credito.Codigo}: crédito de {valor.ToString("C", Cultura.PtBr)} aplicado neste pedido. Saldo do cupom: {credito.Saldo.ToString("C", Cultura.PtBr)}.");
        await uow.CommitAsync();
        return new(uso.Id, credito.Codigo, uso.Valor, credito.Saldo);
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

    // O ChatSite entrega a saída persistida pelo GET/SSE. O lock da conversa/link e a chave do marco
    // impedem repetição; gravar antes do commit preserva o aviso e a alteração na mesma transação.
    private async Task AvisarChatSiteAsync(LinkCardapioConversa link, string marco, string texto, DateTime agora, CancellationToken ct)
    {
        var conversa = await conversas.ObterPorIdAsync(link.EmpresaId, link.ConversaId, ct)
            ?? throw new LinkCardapioConversaIndisponivelException();
        if (conversa.Canal != CanalConversa.ChatSite) return;
        var externoId = PrefixoAvisoChatSite + marco;
        if (await conversas.ObterMensagemPorExternoIdAsync(link.EmpresaId, externoId, ct) is not null) return;
        conversa.RegistrarSaida(agora);
        await conversas.AddMensagemAsync(Mensagem.Saida(link.EmpresaId, link.ConversaId, AutorMensagem.Sistema,
            agora, TipoConteudoMensagem.Texto, texto, externoId), ct);
    }

    private static string TextoPedidoChatSite(PedidoEntity pedido, EnvioComandaCliente envio, CobrancaPedidoResult? cobranca, DateTime agora)
    {
        var texto = $"Recebemos seu pedido. Total: {envio.Total.ToString("C", Cultura.PtBr)}.";
        if (pedido.RequerAprovacao) texto += " A equipe ainda precisa conferir o pedido.";
        if (envio.Saldo <= 0) return texto + " Saldo a pagar: R$ 0,00.";
        if (cobranca is { Status: nameof(StatusCobrancaPedido.Pendente), LinkPagamento: { } link }
            && (cobranca.ExpiraEm is null || cobranca.ExpiraEm > agora))
            return texto + $"\n\nPagamento de {cobranca.Valor.ToString("C", Cultura.PtBr)} pelo link: {link}";
        if (cobranca is { Provedor: CobrancaPedido.ProvedorNaEntrega }
            && PlanoPagamentoEncomenda.TemSinal(pedido.AgendadoParaEm, pedido.CriadoEm))
        {
            var sinal = Math.Min(envio.Saldo, Math.Max(0, PlanoPagamentoEncomenda.Sinal(envio.Total) - (envio.Total - envio.Saldo)));
            return texto + $"\n\nSinal a receber: {sinal.ToString("C", Cultura.PtBr)} por Pix, dinheiro ou maquininha. Restante na entrega: {(envio.Saldo - sinal).ToString("C", Cultura.PtBr)}. Combine o pagamento com a equipe.";
        }
        return texto + $" Saldo a pagar: {envio.Saldo.ToString("C", Cultura.PtBr)}. Combine o pagamento com a equipe.";
    }

    public Task AvisarAsync(Guid empresaId, Guid conversaId, CancellationToken ct) =>
        eventos.PublicarAsync(Evento, empresaId, new { conversaId }, ct);

    /// <summary>Na entrega a combinar, endereço incompleto fica no rascunho e a atendente confere na conversa (#1839).</summary>
    private static EnderecoCheckout? Valido(EnderecoCheckout? e)
    {
        try { return e?.Validar(); }
        catch (RegraDeDominioVioladaException) { return null; }
    }

    private static EnderecoCheckout? Informado(EnderecoCheckout? e) =>
        e is null || new[] { e.Cep, e.Logradouro, e.Numero, e.Bairro, e.Cidade, e.Uf, e.Complemento }.All(string.IsNullOrWhiteSpace) ? null : e;

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
