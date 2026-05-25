using EasyStock.Domain.Events;

namespace EasyStock.Application.Events.Storefront;

/// <summary>
/// Emitido pelo <c>ProcessarWebhookMpUseCase</c> quando um pagamento é aprovado
/// e o Pedido transiciona para <c>AguardandoAprovacaoBaba</c>. Handler downstream
/// envia notificação WhatsApp para a babá responsável (Outbox).
/// </summary>
public sealed record NotificarBabaPedidoNovoEvent(
    Guid PedidoId,
    Guid EmpresaId,
    Guid StorefrontId)
    : DomainEvent(Guid.NewGuid(), DateTime.UtcNow);
