using EasyStock.Domain.Events;

namespace EasyStock.Application.Events.Storefront;

/// <summary>
/// Enfileirado no Outbox quando a Babá recusa um pedido (TASK-EZ-APROVAR-001).
/// O dispatcher (TASK-EZ-APROVAR-002 — fora do escopo desta task) chamará
/// <c>IMercadoPagoClient.RefundAsync(paymentId)</c> com Polly retry 3x.
///
/// <para>
/// Esta task apenas <strong>enfileira</strong>. Refund real fica para
/// TASK-EZ-APROVAR-002 (dispatcher Outbox). Caso o pedido nunca tenha sido
/// pago (caso edge), o handler trata como no-op com log informativo.
/// </para>
/// </summary>
public sealed record EstornarPagamentoAutomaticoEvent(
    Guid PedidoId,
    Guid EmpresaId,
    decimal ValorTotal,
    string Motivo,
    DateTime SolicitadoEm)
    : DomainEvent(Guid.NewGuid(), DateTime.UtcNow);
