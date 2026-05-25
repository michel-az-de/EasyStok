using EasyStock.Domain.Events;

namespace EasyStock.Application.Events.Storefront;

/// <summary>
/// Emitido pelo <c>ProcessarWebhookMpUseCase</c> quando um pagamento é recusado
/// pelo MercadoPago. Handler downstream notifica o cliente via WhatsApp / e-mail
/// (Outbox).
/// </summary>
public sealed record NotificarClientePagamentoRecusadoEvent(
    Guid PedidoId,
    Guid EmpresaId,
    Guid ClienteId,
    string MotivoRecusa)
    : DomainEvent(Guid.NewGuid(), DateTime.UtcNow);
