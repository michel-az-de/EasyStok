using System.Text.Json;
using EasyStock.Application.Ports.Output.Integration;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Domain.Enums.Notifications;
using EasyStock.Domain.Integration;
using EasyStock.Domain.Sales;

namespace EasyStock.Application.Events.Pedidos.Handlers;

/// <summary>
/// Avisa o cliente pelo WhatsApp quando a dona marca um status relevante (S13, RN-32): <c>preparando</c> →
/// <see cref="TipoEventoNotificacao.PedidoEmPreparo"/> (com a previsão da janela), <c>saiu_para_entrega</c> →
/// <see cref="TipoEventoNotificacao.PedidoSaiuParaEntrega"/> e <c>entregue</c> →
/// <see cref="TipoEventoNotificacao.PedidoEntregue"/> (agradecimento, RN-37). Demais status não avisam.
/// Roda ao lado do <see cref="PedidoMudouStatusLogHandler"/>; idempotente por pedido + status novo.
/// </summary>
public sealed class NotificarClienteStatusPedidoHandler(AvisoStatusPedidoCliente aviso) : IIntegrationEventHandler
{
    private static readonly JsonSerializerOptions Camel = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public string TipoEvento => "pedido.mudou_status";

    public async Task HandleAsync(OutboxEventoIntegracao evento, CancellationToken ct)
    {
        var mudanca = JsonSerializer.Deserialize<PedidoMudouStatusEvent>(evento.PayloadJson, Camel);
        if (mudanca is null || TipoDoAviso(mudanca.StatusNovo) is not { } tipo) return;

        await aviso.EnfileirarAsync(tipo, evento.EmpresaId, mudanca.PedidoId, mudanca.StatusNovo, ct);
    }

    /// <summary>Status do pedido que viram aviso ao cliente; nulo para os demais.</summary>
    public static TipoEventoNotificacao? TipoDoAviso(string statusNovo) => statusNovo switch
    {
        StatusPedidoMapper.Preparando => TipoEventoNotificacao.PedidoEmPreparo,
        StatusPedidoMapper.SaiuParaEntrega => TipoEventoNotificacao.PedidoSaiuParaEntrega,
        StatusPedidoMapper.Entregue => TipoEventoNotificacao.PedidoEntregue,
        _ => null,
    };
}
