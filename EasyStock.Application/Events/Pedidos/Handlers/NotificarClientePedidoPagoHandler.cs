using System.Text.Json;
using EasyStock.Application.Ports.Output.Integration;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Domain.Enums.Notifications;
using EasyStock.Domain.Integration;

namespace EasyStock.Application.Events.Pedidos.Handlers;

/// <summary>
/// Pagamento online confirmado (S11, <see cref="PedidoPagoEvent"/>) vira
/// <see cref="TipoEventoNotificacao.PedidoPagoConfirmado"/> ao cliente pelo WhatsApp (S13): "recebemos seu
/// pagamento, pedido nº …, previsão …". Idempotente por pedido (marco <c>pago</c>).
/// </summary>
public sealed class NotificarClientePedidoPagoHandler(AvisoStatusPedidoCliente aviso) : IIntegrationEventHandler
{
    public const string MarcoPago = "pago";

    private static readonly JsonSerializerOptions Camel = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public string TipoEvento => PedidoPagoEvent.TipoEvento;

    public async Task HandleAsync(OutboxEventoIntegracao evento, CancellationToken ct)
    {
        var pago = JsonSerializer.Deserialize<PedidoPagoEvent>(evento.PayloadJson, Camel);
        if (pago is null) return;

        await aviso.EnfileirarAsync(TipoEventoNotificacao.PedidoPagoConfirmado, evento.EmpresaId, pago.PedidoId, MarcoPago, ct);
    }
}
