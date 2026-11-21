using System.Text.Json;
using EasyStock.Application.Events.Pedidos;
using EasyStock.Application.Ports.Output.Integration;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Domain.Integration;
using EasyStock.Domain.Sales;

namespace EasyStock.Application.Events.Atendimento;

/// <summary><c>pedido.pago</c> (#2937): o canhoto em imagem, com o selo "Pago", uma vez por pedido.</summary>
public sealed class CanhotoPedidoPagoHandler(CanhotoPedidoCliente canhoto) : IIntegrationEventHandler
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private sealed record Payload(Guid PedidoId, Guid? ConversaId);

    public string TipoEvento => PedidoPagoEvent.TipoEvento;

    public async Task HandleAsync(OutboxEventoIntegracao evento, CancellationToken ct)
    {
        if (JsonSerializer.Deserialize<Payload>(evento.PayloadJson, Json) is { } e && e.PedidoId != Guid.Empty)
            await canhoto.EnviarAsync(evento.EmpresaId, e.PedidoId, GatilhoCanhoto.Pagamento, e.ConversaId, ct);
    }
}

/// <summary>
/// <c>pedido.mudou_status</c> para "Preparando" (#2937): o canhoto de quem paga na entrega, sem o selo. Os demais
/// status não passam por aqui.
/// </summary>
public sealed class CanhotoPedidoEmPreparoHandler(CanhotoPedidoCliente canhoto) : IIntegrationEventHandler
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private sealed record Payload(Guid PedidoId, string? StatusNovo);

    public string TipoEvento => "pedido.mudou_status";

    public async Task HandleAsync(OutboxEventoIntegracao evento, CancellationToken ct)
    {
        if (JsonSerializer.Deserialize<Payload>(evento.PayloadJson, Json) is { } e && e.PedidoId != Guid.Empty
            && e.StatusNovo == StatusPedidoMapper.Preparando)
            await canhoto.EnviarAsync(evento.EmpresaId, e.PedidoId, GatilhoCanhoto.EmPreparo, null, ct);
    }
}
