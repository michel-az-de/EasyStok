using System.Text.Json;
using EasyStock.Application.Events.Pedidos;
using EasyStock.Application.Ports.Output.Integration;
using EasyStock.Application.UseCases.Campanhas.Interesse;
using EasyStock.Domain.Integration;

namespace EasyStock.Application.Events.Campanhas;

/// <summary>
/// <c>pedido.pago</c> (S11): fecha os interesses abertos do cliente nos itens que ele acabou de pagar
/// (#1228). Fica no outbox, ao lado do aviso ao cliente (S13) e das automáticas (S42), para o checkout
/// não conhecer interesses. O dispatcher roda sem usuário: fixa o tenant do evento antes de ler (RLS).
/// Idempotente: interesse já atendido não entra na busca.
/// </summary>
public sealed class FecharInteressesPedidoPagoHandler(
    ITenantContextAccessor tenant,
    FecharInteressesDoPedidoUseCase fechar) : IIntegrationEventHandler
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Só o que o fechamento usa do <see cref="PedidoPagoEvent"/>.</summary>
    private sealed record PedidoPagoPayload(Guid PedidoId);

    public string TipoEvento => PedidoPagoEvent.TipoEvento;

    public async Task HandleAsync(OutboxEventoIntegracao evento, CancellationToken ct)
    {
        if (JsonSerializer.Deserialize<PedidoPagoPayload>(evento.PayloadJson, Json) is not { } pago) return;

        tenant.SetCurrentTenant(evento.EmpresaId);
        await fechar.ExecuteAsync(evento.EmpresaId, pago.PedidoId, ct);
    }
}
