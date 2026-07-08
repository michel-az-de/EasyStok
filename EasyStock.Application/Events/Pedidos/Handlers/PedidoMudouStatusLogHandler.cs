using EasyStock.Application.Ports.Output.Integration;
using EasyStock.Domain.Integration;

namespace EasyStock.Application.Events.Pedidos.Handlers;

/// <summary>
/// Handler de observabilidade para <c>pedido.mudou_status</c> (Onda 4 / issue 866).
///
/// <para>
/// Registra a transição de status de forma estruturada. Existe por dois motivos:
/// (1) sem NENHUM handler para o TipoEvento, o dispatcher marca o evento como falha e o
/// reagenda em retry (backoff) até esgotar tentativas — lixo no outbox; este handler evita
/// isso marcando o evento como processado; (2) dá rastro operacional das transições até a
/// Onda 5 (#867) plugar os handlers reais de Hiram/marketplace <em>ao lado</em> deste (o
/// dispatcher resolve e roda TODOS os handlers keyed pelo mesmo TipoEvento).
/// </para>
///
/// <para>Idempotente: só loga, sem efeito colateral externo — reprocesso é inócuo.</para>
/// </summary>
public sealed class PedidoMudouStatusLogHandler(ILogger<PedidoMudouStatusLogHandler> logger)
    : IIntegrationEventHandler
{
    public string TipoEvento => "pedido.mudou_status";

    public Task HandleAsync(OutboxEventoIntegracao evento, CancellationToken ct)
    {
        logger.LogInformation(
            "pedido.mudou_status outbox={EventId} empresa={EmpresaId} pedido={PedidoId} payload={Payload}",
            evento.Id, evento.EmpresaId, evento.AggregateId, evento.PayloadJson);
        return Task.CompletedTask;
    }
}
