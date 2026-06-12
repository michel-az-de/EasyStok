using EasyStock.Application.Services;
using EasyStock.Application.UseCases.CriarPedido;
using EasyStock.Application.UseCases.Pedidos;
using EasyStock.Domain.Enums.Financeiro;
using EasyStock.Domain.Sales;

namespace EasyStock.Application.UseCases.CancelarPedido;

public sealed record CancelarPedidoCommand(
    [property: Required] Guid EmpresaId,
    [property: Required] Guid Id,
    Guid? UsuarioId = null,
    [property: MaxLength(120)] string? UsuarioNome = null,
    string? Motivo = null,
    [property: MaxLength(20)] string? Origem = "web");

/// <summary>
/// Cancela um pedido COM estorno em cascata, numa transacao unica:
///   1. Se o pedido ja tinha baixado estoque (Pronto/Entregue), DEVOLVE o estoque
///      via <see cref="PedidoEstoqueIntegrationService.DevolverAsync"/> (idempotente).
///   2. Muda o status para Cancelado.
///   3. Cancela a ContaReceber gerada deste pedido, se houver (Origem=Pedido).
///   4. O CAIXA estorna sozinho por agregacao: a soma de PedidoPagamento do dia
///      filtra Status != cancelado, entao o pagamento sai do caixa automaticamente.
///
/// Antes, este use case SO mudava o status (estoque ficava baixado, ContaReceber
/// ficava aberta) — bug de integridade quando o cancelamento nao passava por
/// AtualizarStatusPedidoUseCase. Espelha agora o desconto/devolucao daquele use case.
/// </summary>
public class CancelarPedidoUseCase(
    IPedidoRepository pedidoRepo,
    PedidoEstoqueIntegrationService estoqueIntegration,
    IContaReceberRepository contaReceberRepo,
    IUnitOfWork uow,
    ILogger<CancelarPedidoUseCase> logger)
{
    public async Task<PedidoResult?> ExecuteAsync(CancelarPedidoCommand cmd)
    {
        UseCaseGuards.EnsureEmpresaId(cmd.EmpresaId);
        UseCaseGuards.EnsureNotEmpty(cmd.Id, "Id");

        // CRITICAL: WithDetails carrega Itens. Sem isso, DevolverAsync vira no-op
        // silencioso (mesma armadilha documentada em AtualizarStatusPedidoUseCase).
        var pedido = await pedidoRepo.GetByIdWithDetailsAsync(cmd.EmpresaId, cmd.Id);
        if (pedido == null) return null;
        if (pedido.StatusEnum == StatusPedido.Cancelado) return CriarPedidoUseCase.Map(pedido);

        var statusAntigo = pedido.Status;

        // 1. Estorno de estoque ANTES de mudar o status. Idempotente via DocumentoReferencia
        //    (refDocItem = pedidoId:itemId). Compoe no mesmo CommitAsync (DevolverAsync nao commita).
        var eraEstoqueDescontado = PedidoStateMachine.DescontaEstoque(pedido.StatusEnum);
        if (eraEstoqueDescontado)
            await estoqueIntegration.DevolverAsync(pedido);

        // 2. Transicao de status (delega a PedidoStateMachine; idempotente e validado).
        pedido.Cancelar();

        await pedidoRepo.AddEventoAsync(new PedidoEvento
        {
            Id = Guid.NewGuid(),
            PedidoId = pedido.Id,
            Tipo = "cancelado",
            StatusAntigo = statusAntigo,
            StatusNovo = StatusPedidoMapper.Cancelado,
            Detalhes = cmd.Motivo,
            UsuarioId = cmd.UsuarioId,
            UsuarioNome = cmd.UsuarioNome,
            Origem = cmd.Origem,
            OcorridoEm = DateTime.UtcNow
        });

        await pedidoRepo.UpdateAsync(pedido);

        // 3. Estorno financeiro: cancela a ContaReceber gerada deste pedido (se houver e
        //    ainda nao cancelada). Caixa nao precisa de acao: estorna por agregacao.
        var contaReceber = await contaReceberRepo.GetByOrigemAsync(
            cmd.EmpresaId, OrigemContaFinanceira.Pedido, pedido.Id);
        var contaReceberCancelada = false;
        if (contaReceber is not null
            && contaReceber.Status != StatusContaFinanceira.Cancelada
            && contaReceber.Status != StatusContaFinanceira.Paga)
        {
            // Cancelar lanca se a conta estiver Paga; por isso so cancelamos quando
            // ainda esta cancelavel (Aberta/Rascunho/ParcialmentePaga/Vencida).
            contaReceber.Cancelar(cmd.Motivo ?? "Pedido cancelado", cmd.UsuarioId);
            await contaReceberRepo.UpdateAsync(contaReceber);
            contaReceberCancelada = true;
        }
        else if (contaReceber is not null && contaReceber.Status == StatusContaFinanceira.Paga)
        {
            // CR paga: o estorno do pagamento (e do movimento de caixa associado a parcela)
            // e cascata maior (EstornarPagamentoParcela) — fica pra fatia de estorno financeiro.
            logger.LogWarning(
                "Pedido {Id} cancelado, mas ContaReceber {CrId} esta PAGA — nao cancelada automaticamente. " +
                "Estorne os pagamentos da conta manualmente.", pedido.Id, contaReceber.Id);
        }

        await uow.CommitAsync();

        logger.LogInformation(
            "Pedido {Id} cancelado (motivo={Motivo}); estoque devolvido={Devolvido}, contaReceber cancelada={CrCancelada}.",
            pedido.Id, cmd.Motivo ?? "—", eraEstoqueDescontado, contaReceberCancelada);
        return CriarPedidoUseCase.Map(pedido);
    }
}
