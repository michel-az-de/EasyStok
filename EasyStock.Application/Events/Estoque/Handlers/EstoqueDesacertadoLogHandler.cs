using EasyStock.Application.Ports.Output.Integration;
using EasyStock.Domain.Integration;

namespace EasyStock.Application.Events.Estoque.Handlers;

/// <summary>
/// Handler de observabilidade para <c>estoque.desacerto</c> (S17). Sem nenhum handler o
/// dispatcher reagenda o evento em retry até esgotar tentativas; este marca como processado
/// e deixa rastro até S18/S22 plugarem os consumidores reais ao lado.
/// Idempotente: só loga.
/// </summary>
public sealed class EstoqueDesacertadoLogHandler(ILogger<EstoqueDesacertadoLogHandler> logger)
    : IIntegrationEventHandler
{
    public string TipoEvento => EstoqueDesacertadoEvent.TipoEventoOutbox;

    public Task HandleAsync(OutboxEventoIntegracao evento, CancellationToken ct)
    {
        logger.LogWarning(
            "estoque.desacerto outbox={EventId} empresa={EmpresaId} pedido={PedidoId} payload={Payload}",
            evento.Id, evento.EmpresaId, evento.AggregateId, evento.PayloadJson);
        return Task.CompletedTask;
    }
}
