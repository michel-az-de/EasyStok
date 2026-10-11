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
using EasyStock.Domain.Sales;

namespace EasyStock.Application.UseCases.Atendimento.Comanda;

public sealed record ConfirmarAlteracaoComandaInput(Guid EmpresaId, Guid ConversaId, Guid LinkId, bool Aceitar,
    Guid UsuarioId, string? UsuarioNome, NivelAcesso Nivel, bool Automatico = false);

public sealed class ConfirmarAlteracaoComandaUseCase(ILinkCardapioConversaRepository links, IConversaRepository conversas,
    IPedidoRepository pedidos, IStorefrontRepository vitrines, CheckoutCoreService checkout,
    ComandaClienteUseCase comanda, SaldoFinanceiroPedido saldos, ICreditoClienteComandaRepository creditos,
    IEstornosOnlineService estornos, GerarCobrancaPedidoUseCase cobrar, ICobrancaPedidoRepository cobrancas,
    PedidoEstoqueIntegrationService estoque, AvisoCobrancaConversa aviso, IUnitOfWork uow, TimeProvider relogio)
{
    public async Task<EnvioComandaCliente> ExecuteAsync(ConfirmarAlteracaoComandaInput input, CancellationToken ct)
    {
        var estornosParaRetomar = new List<Guid>();
        var alterado = false;
        var result = await uow.ExecuteInTransactionSemRetryAsync(async t =>
        {
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
            alterado = true;
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
            if (devolucao > 0 && input.Nivel is not (NivelAcesso.Gerente or NivelAcesso.Admin or NivelAcesso.SuperAdmin))
                throw new UnauthorizedAccessException("Só a dona ou um gerente pode confirmar uma devolução ou cupom.");
            if (input.Automatico && (devolucao > 0 || !SomenteAcrescimos(antigos, selecao)))
                throw new ComandaClienteConflitoException("Esta alteração precisa ser conferida pelo atendimento.");
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
                c.Cancelar(GerarCobrancaPedidoUseCase.MotivoValorAlterado, agora);
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
            await comanda.AplicarCupomAsync(pedido, dados.CodigoCupom, t);
            var saldo = pedido.Total.Valor - (await saldos.ConsultarAsync(pedido, t)).Alocado;
            CobrancaPedidoResult? cobranca = null;
            if (saldo > 0) cobranca = await cobrar.ExecuteAsync(new(input.EmpresaId, pedido.Id, input.ConversaId, CobrarSaldo: true), t);
            var situacao = devolucao > 0 ? codigoCupom is null ? "estorno_pendente" : "cupom_emitido"
                : saldo > 0 ? "aguardando_pagamento" : "alteracao_confirmada";
            var resposta = new EnvioComandaCliente(pedido.Id, situacao, pedido.Total.Valor, saldo, cobranca?.LinkPagamento, codigoCupom);
            link.RegistrarResultado(pedido.Id, JsonSerializer.Serialize(resposta, ComandaClienteUseCase.Json));
            await uow.CommitAsync();
            return resposta;
        }, ct);
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
                alterado = true;
            }
        }
        await comanda.AvisarAsync(input.EmpresaId, input.ConversaId, ct);
        if (alterado) await aviso.EnviarAsync(input.EmpresaId, input.ConversaId, Texto(result), relogio.GetUtcNow().UtcDateTime, ct);
        return result;
    }

    public static bool SomenteAcrescimos(IEnumerable<PedidoItem> antigos, IReadOnlyList<ItemSelecionadoComanda> selecao) =>
        antigos.GroupBy(a => (a.CardapioItemId, a.CardapioItemVariacaoId)).All(g =>
            selecao.Any(n => n.CardapioItemId == g.Key.CardapioItemId && n.VariacaoId == g.Key.CardapioItemVariacaoId
                && n.Qtd >= g.Sum(a => a.Quantidade) && g.All(a => (n.Observacao ?? "") == (a.Observacao ?? ""))));
    public static Guid IdEstorno(Guid linkId, Guid pagamentoId) => new(SHA256.HashData(Encoding.UTF8.GetBytes($"comanda:{linkId:N}:{pagamentoId:N}"))[..16]);
    private static string Texto(EnvioComandaCliente r) => r.Situacao == "alteracao_recusada"
        ? "A alteração precisa de outro ajuste. Seu pedido original foi mantido; fale com o atendimento."
        : $"Comanda atualizada. Total {r.Total.ToString("C", Cultura.PtBr)}. " + (r.CodigoCupom is { } cupom
            ? $"Seu cupom para uma próxima compra: {cupom}."
            : r.Situacao == "estorno_pendente" ? "A devolução foi solicitada e está em acompanhamento."
            : r.Situacao == "estorno_confirmado" ? "A devolução foi confirmada pelo Mercado Pago."
            : r.LinkPagamento is { } link ? $"Pague apenas a diferença: {link}" : "Seu pagamento já cobre esse pedido.");
}
