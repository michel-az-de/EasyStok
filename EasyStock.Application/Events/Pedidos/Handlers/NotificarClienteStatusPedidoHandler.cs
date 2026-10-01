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
/// Preparo e saída respeitam os avisos desligados pelo cliente (S24); o agradecimento sai sempre.
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

        await aviso.EnfileirarAsync(tipo, evento.EmpresaId, mudanca.PedidoId, mudanca.StatusNovo, ct,
            respeitaPreferenciaAvisos: tipo != TipoEventoNotificacao.PedidoEntregue);

        // S26: pedido de avaliação em dois botões 30 min após a entrega. É pós-venda: respeita os avisos
        // desligados pelo cliente (o agradecimento acima é incondicional).
        if (tipo == TipoEventoNotificacao.PedidoEntregue)
            await aviso.EnfileirarAsync(TipoEventoNotificacao.AvaliacaoSolicitada, evento.EmpresaId, mudanca.PedidoId,
                MarcoAvaliacao, ct, EntregueEm(mudanca).Add(AtrasoAvaliacao), respeitaPreferenciaAvisos: true);
    }

    /// <summary>Marco da chave de idempotência do pedido de avaliação (um por pedido).</summary>
    public const string MarcoAvaliacao = "avaliacao";

    /// <summary>Atraso do pedido de avaliação após a entrega (S26, RN-34).</summary>
    public static readonly TimeSpan AtrasoAvaliacao = TimeSpan.FromMinutes(30);

    private static DateTime EntregueEm(PedidoMudouStatusEvent mudanca) =>
        mudanca.OcorridoEm.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(mudanca.OcorridoEm, DateTimeKind.Utc)
            : mudanca.OcorridoEm.ToUniversalTime();

    /// <summary>Status do pedido que viram aviso ao cliente; nulo para os demais.</summary>
    public static TipoEventoNotificacao? TipoDoAviso(string statusNovo) => statusNovo switch
    {
        StatusPedidoMapper.Preparando => TipoEventoNotificacao.PedidoEmPreparo,
        StatusPedidoMapper.SaiuParaEntrega => TipoEventoNotificacao.PedidoSaiuParaEntrega,
        StatusPedidoMapper.Entregue => TipoEventoNotificacao.PedidoEntregue,
        _ => null,
    };
}
