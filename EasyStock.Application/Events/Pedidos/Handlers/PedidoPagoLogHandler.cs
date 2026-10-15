using EasyStock.Application.Ports.Output.Integration;
using EasyStock.Domain.Integration;

namespace EasyStock.Application.Events.Pedidos.Handlers;

/// <summary>
/// Handler de observabilidade para <c>pedido.pago</c> (S11). Mesmo motivo do
/// <see cref="PedidoMudouStatusLogHandler"/>: sem handler o dispatcher reagenda o evento em retry até
/// esgotar. S13 (aviso), S18 (esteira) e S20 (impressão) entram ao lado deste.
/// </summary>
public sealed class PedidoPagoLogHandler(ILogger<PedidoPagoLogHandler> logger) : IIntegrationEventHandler
{
    public string TipoEvento => PedidoPagoEvent.TipoEvento;

    public Task HandleAsync(OutboxEventoIntegracao evento, CancellationToken ct)
    {
        logger.LogInformation(
            "pedido.pago outbox={EventId} empresa={EmpresaId} pedido={PedidoId}",
            evento.Id, evento.EmpresaId, evento.AggregateId);
        return Task.CompletedTask;
    }
}
