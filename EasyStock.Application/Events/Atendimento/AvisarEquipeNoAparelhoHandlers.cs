using System.Text.Json;
using EasyStock.Application.Events.Pedidos;
using EasyStock.Application.Ports.Output.Integration;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Domain.Integration;

namespace EasyStock.Application.Events.Atendimento;

/// <summary>Conversa aberta pela primeira mensagem do contato (S42) vira aviso "Contato novo" no aparelho (#1807).</summary>
public sealed class AvisarEquipeContatoNovoHandler(AvisoEquipeNoAparelho aviso) : IIntegrationEventHandler
{
    public string TipoEvento => ConversaAbertaEvent.TipoEvento;

    public async Task HandleAsync(OutboxEventoIntegracao evento, CancellationToken ct)
    {
        var aberta = JsonSerializer.Deserialize<ConversaAbertaEvent>(evento.PayloadJson, AvisoNoAparelhoJson.Opcoes);
        if (aberta is null) return;
        await aviso.ContatoNovoAsync(evento.EmpresaId, aberta.ConversaId, ct);
    }
}

/// <summary>Pagamento confirmado (S11, <see cref="PedidoPagoEvent"/>) vira aviso "Pagamento confirmado" no aparelho (#1807).</summary>
public sealed class AvisarEquipePedidoPagoHandler(AvisoEquipeNoAparelho aviso) : IIntegrationEventHandler
{
    public string TipoEvento => PedidoPagoEvent.TipoEvento;

    public async Task HandleAsync(OutboxEventoIntegracao evento, CancellationToken ct)
    {
        var pago = JsonSerializer.Deserialize<PedidoPagoEvent>(evento.PayloadJson, AvisoNoAparelhoJson.Opcoes);
        if (pago is null) return;
        await aviso.PedidoPagoAsync(evento.EmpresaId, pago.PedidoId, ct);
    }
}

/// <summary>Pedido que muda de etapa da esteira (<c>pedido.mudou_status</c>) vira aviso no aparelho (#1807).</summary>
public sealed class AvisarEquipeEtapaDoPedidoHandler(AvisoEquipeNoAparelho aviso) : IIntegrationEventHandler
{
    public string TipoEvento => "pedido.mudou_status";

    public async Task HandleAsync(OutboxEventoIntegracao evento, CancellationToken ct)
    {
        var mudanca = JsonSerializer.Deserialize<PedidoMudouStatusEvent>(evento.PayloadJson, AvisoNoAparelhoJson.Opcoes);
        if (mudanca is null) return;
        await aviso.EtapaDoPedidoAsync(evento.EmpresaId, mudanca.PedidoId, mudanca.StatusNovo, ct);
    }
}

internal static class AvisoNoAparelhoJson
{
    public static readonly JsonSerializerOptions Opcoes = new(JsonSerializerDefaults.Web);
}
