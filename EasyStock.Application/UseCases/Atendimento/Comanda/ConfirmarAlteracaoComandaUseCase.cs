using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Ports.Output.Persistence.Pagamentos;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.Services;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Application.Services.Pedidos;
using EasyStock.Application.Services.Storefront;
using EasyStock.Application.UseCases.Pedidos.Cobranca;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Entities.Pagamentos;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.Domain.Sales;

namespace EasyStock.Application.UseCases.Atendimento.Comanda;

public sealed record ConfirmarAlteracaoComandaInput(Guid EmpresaId, Guid ConversaId, Guid LinkId, bool Aceitar,
    Guid UsuarioId, string? UsuarioNome, NivelAcesso Nivel, bool Automatico = false);

public sealed class ConfirmarAlteracaoComandaUseCase(ILinkCardapioConversaRepository links, IConversaRepository conversas,
    IPedidoRepository pedidos, IStorefrontRepository vitrines, CheckoutCoreService checkout,
    ComandaClienteUseCase comanda, SaldoFinanceiroPedido saldos, ICreditoClienteComandaRepository creditos,
    IEstornosOnlineService estornos, GerarCobrancaPedidoUseCase cobrar, ICobrancaPedidoRepository cobrancas,
    PedidoEstoqueIntegrationService estoque, AvisoStatusPedidoCliente aviso, IUnitOfWork uow, TimeProvider relogio,
    ExpiracaoPreferenciaMercadoPago expiracao, ILogger<ConfirmarAlteracaoComandaUseCase> logger)
{
    public async Task<EnvioComandaCliente> ExecuteAsync(ConfirmarAlteracaoComandaInput input, CancellationToken ct)
    {
        var estornosParaRetomar = new List<Guid>();
        var substituidas = new List<string>();
        bool? cobrarSaldo = null;
        var result = await uow.ExecuteInTransactionSemRetryAsync(async t =>
        {
            estornosParaRetomar.Clear();
            substituidas.Clear();
            cobrarSaldo = null;
            var link = await links.ObterComLockAsync(input.EmpresaId, input.LinkId, t);
            if (link is null || link.ConversaId != input.ConversaId) throw new ComandaClienteConflitoException("Alteração não encontrada.");
            var anterior = ComandaClienteUseCase.Ler<EnvioComandaCliente>(link.ResultadoJson);
            if (link.UsadoEm is null || anterior is null) throw new ComandaClienteConflitoException("O cliente ainda não enviou esta seleção.");
            if (anterior.Situacao != "alteracao_aguardando_confirmacao")
            {
                if (anterior.Situacao == "estorno_pendente")
                {
                    var pendentes = await estornos.ConsultarAsync(input.EmpresaId, anterior.PedidoId, t);
                    estornosParaRetomar.AddRange(pendentes.Estornos.Where(e => e.Situacao == PedidoEstornoOnline.Pendente
                        && e.Id == IdEstorno(link.Id, e.PagamentoId)).Select(e => e.Id));
                }
                return anterior;
            }
            var dados = ComandaClienteUseCase.Ler<RascunhoComandaCliente>(link.RascunhoJson)
                ?? throw new ComandaClienteConflitoException("Seleção não encontrada.");
            await pedidos.TravarAsync(input.EmpresaId, anterior.PedidoId, t);
            var pedido = await pedidos.GetByIdWithDetailsAsync(input.EmpresaId, anterior.PedidoId)
                ?? throw new ComandaClienteConflitoException("Pedido não encontrado.");
            var conversa = await conversas.ObterPorIdAsync(input.EmpresaId, input.ConversaId, t);
            if (pedido.EmpresaId != input.EmpresaId || conversa is null || conversa.ClienteId != pedido.ClienteId || !conversa.EstaAberta)
                throw new ComandaClienteConflitoException("Pedido não pertence à conversa.");
            if (!input.Aceitar)
            {
                var recusado = anterior with { Situacao = "alteracao_recusada" };
                link.RegistrarResultado(pedido.Id, JsonSerializer.Serialize(recusado, ComandaClienteUseCase.Json));
                await aviso.EnfileirarAsync(Domain.Enums.Notifications.TipoEventoNotificacao.ComandaAtualizada,
                    input.EmpresaId, pedido.Id, $"comanda:{link.Id:N}:recusada", t, mensagem: Texto(recusado));
                await uow.CommitAsync();
                return recusado;
            }
            if (!ComandaClienteUseCase.PodeAlterar(pedido) || pedido.AlteradoEm != dados.PedidoAtualizadoEm
                && (dados.PedidoResumoHash is null || dados.PedidoResumoHash != ComandaClienteUseCase.ResumoHash(pedido)))
                throw new ComandaClienteConflitoException("O pedido mudou desde a solicitação. Gere um novo link para o cliente conferir.");
            var selecao = await comanda.SelecionarAsync(input.EmpresaId, dados.Itens, pedido, t);
            var antigos = pedido.Itens.Where(i => i.CardapioItemId is not null).ToList();
            var frete = pedido.Itens.Except(antigos).Sum(i => i.Subtotal);
            var novoTotal = selecao.Sum(i => i.Qtd * i.Preco) + frete;
            var saldoAnterior = await saldos.ConsultarAsync(pedido, t);
            if (saldoAnterior.Reservado > 0) throw new ComandaClienteConflitoException("Aguarde a devolução em andamento antes de alterar de novo.");
            var devolucao = Math.Max(0, saldoAnterior.Alocado - novoTotal);
            if (input.Automatico && (devolucao > 0 || (dados.Observacoes ?? "") != (pedido.Observacoes ?? "")
                || !SomenteAcrescimos(antigos, selecao)))
                return anterior;
            if (devolucao > 0 && input.Nivel is not (NivelAcesso.Gerente or NivelAcesso.Admin or NivelAcesso.SuperAdmin))
                throw new UnauthorizedAccessException("Só a dona ou um gerente pode confirmar uma devolução ou cupom.");
            var agora = relogio.GetUtcNow().UtcDateTime;
            var historico = JsonSerializer.Serialize(antigos.Select(i => new { i.Nome, i.Quantidade, i.PrecoUnitario, i.Observacao }), ComandaClienteUseCase.Json);
            // Retorna exclusivamente as saídas das linhas anteriores. Novas linhas recebem novos IDs.
            if (PedidoStateMachine.DescontaEstoque(pedido.StatusEnum))
                foreach (var item in antigos) await estoque.DevolverItemAsync(pedido, item, t);
            foreach (var item in antigos) pedido.Itens.Remove(item);
            var loja = await vitrines.GetByEmpresaAsync(input.EmpresaId, t) ?? throw new UseCaseValidationException("Cardápio indisponível.");
            var menu = await checkout.CarregarItensCardapioAsync(loja.Id, dados.Itens.Select(i => i.CardapioItemId), t);
            var novos = await checkout.AdicionarItensAsync(pedido, dados.Itens, menu, t);
            for (var i = 0; i < novos.Count; i++)
            {
                novos[i].PrecoUnitario = selecao[i].Preco;
                novos[i].RecalcularSubtotal();
                if (!pedido.Itens.Contains(novos[i])) pedido.Itens.Add(novos[i]);
            }
            var statusAnterior = pedido.Status;
            if (pedido.StatusEnum == StatusPedido.Pronto) pedido.RetomarPreparoPorAlteracao();
            pedido.Observacoes = dados.Observacoes;
            pedido.RecalcularTotal();
            await pedidos.AddEventoAsync(new() { Id = Guid.NewGuid(), PedidoId = pedido.Id, Tipo = "comanda_cliente_alterada",
                UsuarioId = input.UsuarioId == Guid.Empty ? null : input.UsuarioId, UsuarioNome = input.UsuarioNome,
                Origem = "comanda_cliente", OcorridoEm = agora, Detalhes = $"Antes: {historico}. Total: {novoTotal}. Link: {link.Id}. {statusAnterior} -> {pedido.Status}" });
            foreach (var c in (await cobrancas.ListarDoPedidoAsync(input.EmpresaId, pedido.Id, t)).Where(c => c.EstaPendente))
            {
                c.Cancelar(GerarCobrancaPedidoUseCase.MotivoValorAlterado, agora);
                // #1717-8: o link cancelado para de aceitar pagamento no provedor depois do commit.
                if (c.EhOnline && c.ReferenciaExterna is { } preferencia) substituidas.Add(preferencia);
            }
            await pedidos.UpdateAsync(pedido);
            await uow.CommitAsync();
            string? codigoCupom = null;
            if (devolucao > 0)
            {
                if (dados.Acerto == "cupom")
                {
                    var credito = CreditoClienteComanda.Criar(input.EmpresaId, pedido.ClienteId!.Value, pedido.Id, link.Id, devolucao, agora);
                    await creditos.AddAsync(credito, t);
                    codigoCupom = credito.Codigo;
                    await uow.CommitAsync();
                }
                else
                {
                    var restantes = devolucao;
                    var pagamentos = await estornos.ConsultarAsync(input.EmpresaId, pedido.Id, t);
                    foreach (var pagamento in pagamentos.Pagamentos.Where(p => p.Disponivel > 0).OrderByDescending(p => p.PagoEm))
                    {
                        var valor = Math.Min(restantes, pagamento.Disponivel);
                        var operacaoId = IdEstorno(link.Id, pagamento.Id);
                        await estornos.ReservarAsync(new(input.EmpresaId, pedido.Id, operacaoId, pagamento.Id, valor,
                            "Redução da comanda solicitada pelo cliente", input.UsuarioId, input.UsuarioNome, input.Nivel), t);
                        estornosParaRetomar.Add(operacaoId);
                        restantes -= valor;
                        if (restantes == 0) break;
                    }
                    if (restantes > 0) throw new UseCaseValidationException("O pagamento exige devolução manual. Registre a devolução pela Ficha ou combine cupom com o cliente.");
                }
            }
            var cupomAplicado = await comanda.AplicarCupomAsync(pedido, dados.CodigoCupom, t, avisar: false);
            var alocado = (await saldos.ConsultarAsync(pedido, t)).Alocado;
            var saldo = pedido.Total.Valor - alocado;
            if (saldo > 0)
            {
                // #1725-6: a cobrança sai depois do commit, sem o link travado durante a chamada ao provedor. Até lá, outro
                // aceite recebe este resultado provisório, sem link. #1717-7: encomenda sem nada alocado cobra o sinal.
                cobrarSaldo = !(PlanoPagamentoEncomenda.TemSinal(pedido.AgendadoParaEm, pedido.CriadoEm) && alocado <= 0);
                var provisorio = new EnvioComandaCliente(pedido.Id, "aguardando_pagamento", pedido.Total.Valor, saldo,
                    CodigoCupom: codigoCupom, CupomAplicado: cupomAplicado, EmCobranca: true);
                link.RegistrarResultado(pedido.Id, JsonSerializer.Serialize(provisorio, ComandaClienteUseCase.Json));
                await uow.CommitAsync();
                return provisorio;
            }
            var situacao = devolucao > 0 ? codigoCupom is null ? "estorno_pendente" : "cupom_emitido" : "alteracao_confirmada";
            var resposta = new EnvioComandaCliente(pedido.Id, situacao, pedido.Total.Valor, saldo, null, codigoCupom, cupomAplicado);
            link.RegistrarResultado(pedido.Id, JsonSerializer.Serialize(resposta, ComandaClienteUseCase.Json));
            await AvisarAsync(input, link.Id, resposta, devolucao, null, t);
            await uow.CommitAsync();
            return resposta;
        }, ct);
        await expiracao.ExpirarAsync(substituidas, CancellationToken.None);
        if (result.EmCobranca && cobrarSaldo is { } complementar)
            result = await CobrarDepoisDoCommitAsync(input, result, complementar);
        foreach (var id in estornosParaRetomar) await estornos.RetomarAsync(input.EmpresaId, result.PedidoId, id, input.Nivel, ct);
        if (result.Situacao == "estorno_pendente")
        {
            var historico = await estornos.ConsultarAsync(input.EmpresaId, result.PedidoId, ct);
            var proprios = historico.Estornos.Where(e => e.Id == IdEstorno(input.LinkId, e.PagamentoId)).ToList();
            if (proprios.Count > 0 && proprios.All(e => e.Situacao == PedidoEstornoOnline.Confirmado))
            {
                result = result with { Situacao = "estorno_confirmado", Saldo = 0 };
                var atualizado = result;
                await uow.ExecuteInTransactionSemRetryAsync(async t =>
                {
                    var link = await links.ObterComLockAsync(input.EmpresaId, input.LinkId, t);
                    link!.RegistrarResultado(result.PedidoId, JsonSerializer.Serialize(atualizado, ComandaClienteUseCase.Json));
                    await uow.CommitAsync();
                    return true;
                }, ct);
            }
        }
        await comanda.AvisarAsync(input.EmpresaId, input.ConversaId, ct);
        return result;
    }

    /// <summary>
    /// Cobrança da diferença da alteração já commitada (#1725-6), como a reemissão do console: trava só o pedido durante a
    /// chamada ao provedor. Sem link possível (provedor fora ou sem credencial), a diferença fica a receber na entrega e a
    /// equipe é avisada na conversa (#1726): a alteração aplicada nunca fica sem cobrança. Sem o token da requisição: com a
    /// alteração gravada, o resultado final precisa ser registrado mesmo se a página fechar.
    /// </summary>
    private async Task<EnvioComandaCliente> CobrarDepoisDoCommitAsync(ConfirmarAlteracaoComandaInput input,
        EnvioComandaCliente provisorio, bool cobrarSaldo)
    {
        CobrancaPedidoResult? cobranca = null;
        try
        {
            cobranca = await cobrar.ExecuteComTravaAsync(new(input.EmpresaId, provisorio.PedidoId, input.ConversaId,
                CobrarSaldo: cobrarSaldo, NaEntregaSemProvedor: true), CancellationToken.None);
        }
        catch (CobrancaPedidoConflitoException ex)
        {
            // Pago ou cancelado no intervalo: o resultado fica sem link e a Ficha mostra o estado atual do pedido.
            logger.LogWarning(ex, "Alteracao da comanda sem cobranca pedidoId={PedidoId}", provisorio.PedidoId);
        }

        return await uow.ExecuteInTransactionSemRetryAsync(async t =>
        {
            var link = await links.ObterComLockAsync(input.EmpresaId, input.LinkId, t)
                ?? throw new ComandaClienteConflitoException("Alteração não encontrada.");
            var resposta = provisorio with { LinkPagamento = cobranca?.LinkPagamento, EmCobranca = false };
            if (cobranca?.Provedor == CobrancaPedido.ProvedorNaEntrega)
                await conversas.AddMensagemAsync(Mensagem.Saida(input.EmpresaId, input.ConversaId, AutorMensagem.Sistema,
                    relogio.GetUtcNow().UtcDateTime, TipoConteudoMensagem.Texto,
                    $"Link do Mercado Pago indisponível na alteração da comanda: {cobranca.Valor.ToString("C", Cultura.PtBr)} " +
                    "ficam para receber na entrega. Para mandar um link, use Link de pagamento na Ficha."), t);
            link.RegistrarResultado(resposta.PedidoId, JsonSerializer.Serialize(resposta, ComandaClienteUseCase.Json));
            await AvisarAsync(input, link.Id, resposta, 0, cobranca, t);
            await uow.CommitAsync();
            return resposta;
        }, CancellationToken.None);
    }

    private async Task AvisarAsync(ConfirmarAlteracaoComandaInput input, Guid linkId, EnvioComandaCliente resposta, decimal devolucao,
        CobrancaPedidoResult? cobranca, CancellationToken ct)
    {
        if (resposta.CupomAplicado is { } cupom) await comanda.AvisarCupomAsync(input.EmpresaId, resposta.PedidoId, cupom, ct);
        await aviso.EnfileirarAsync(Domain.Enums.Notifications.TipoEventoNotificacao.ComandaAtualizada,
            input.EmpresaId, resposta.PedidoId, $"comanda:{linkId:N}:{resposta.Situacao}", ct,
            mensagem: Texto(resposta, devolucao, cobranca?.LinkPagamento is null ? null : cobranca.Valor));
    }

    public static bool SomenteAcrescimos(IEnumerable<PedidoItem> antigos, IReadOnlyList<ItemSelecionadoComanda> selecao) =>
        antigos.GroupBy(a => (a.CardapioItemId, a.CardapioItemVariacaoId)).All(g =>
            selecao.Any(n => n.CardapioItemId == g.Key.CardapioItemId && n.VariacaoId == g.Key.CardapioItemVariacaoId
                && n.Qtd >= g.Sum(a => a.Quantidade) && g.All(a => (n.Observacao ?? "") == (a.Observacao ?? ""))));
    public static Guid IdEstorno(Guid linkId, Guid pagamentoId) => new(SHA256.HashData(Encoding.UTF8.GetBytes($"comanda:{linkId:N}:{pagamentoId:N}"))[..16]);
    /// <param name="valorDoLink">Quanto o link cobra; menor que o saldo na encomenda que cobra o sinal (#1717-7).</param>
    private static string Texto(EnvioComandaCliente r, decimal devolucao = 0, decimal? valorDoLink = null) => r.Situacao == "alteracao_recusada"
        ? "A alteração precisa de outro ajuste. Seu pedido original foi mantido; fale com o atendimento."
        : $"Seu pedido foi atualizado. Total: {r.Total.ToString("C", Cultura.PtBr)}.\n" + (r.CodigoCupom is { } cupom
            ? $"Você tem {devolucao.ToString("C", Cultura.PtBr)} de crédito para uma próxima compra.\nCódigo do cupom: {cupom}\nEsse crédito não é uma devolução em dinheiro."
            : r.Situacao == "estorno_pendente" ? $"Solicitamos a devolução de {devolucao.ToString("C", Cultura.PtBr)} pelo Mercado Pago. Aguarde a confirmação."
            : r.LinkPagamento is { } link && valorDoLink is { } sinal && sinal < r.Saldo
                ? $"Falta pagar {r.Saldo.ToString("C", Cultura.PtBr)}. Sinal de {sinal.ToString("C", Cultura.PtBr)} pelo link: {link}\nRestante na entrega: {(r.Saldo - sinal).ToString("C", Cultura.PtBr)}."
            : r.LinkPagamento is { } diferenca ? $"Falta pagar {Math.Max(0, r.Saldo).ToString("C", Cultura.PtBr)}. Pague apenas essa diferença: {diferenca}"
            : r.Saldo > 0 ? $"Falta pagar {r.Saldo.ToString("C", Cultura.PtBr)}. Combine o pagamento com a equipe."
            : "Não há mais valor a pagar por esse pedido.");
}
