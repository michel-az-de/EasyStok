namespace EasyStock.Domain.Events.Storefront;

/// <summary>
/// Emitido quando um Pedido Storefront é cancelado — seja por timeout do background
/// service (AguardandoPagamento > 30 min) ou por cancelamento explícito.
/// Handler <c>LiberarVagaOnPedidoCanceladoHandler</c> libera a VagaOcupada associada (ADR-0014).
/// </summary>
public sealed record PedidoCanceladoEvent(
    Guid PedidoId,
    Guid StorefrontId,
    string Motivo)
    : DomainEvent(Guid.NewGuid(), DateTime.UtcNow);
