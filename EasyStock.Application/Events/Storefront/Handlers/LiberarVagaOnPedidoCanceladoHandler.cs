using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Domain.Events.Storefront;

namespace EasyStock.Application.Events.Storefront.Handlers;

/// <summary>
/// Libera a <c>VagaOcupada</c> quando um Pedido Storefront é cancelado (ADR-0014 §Solução 3).
/// <strong>Idempotente</strong>: <see cref="IVagaOcupadaRepository.LiberarPorPedidoAsync"/>
/// é no-op se a vaga já foi liberada — safe em contextos at-least-once.
/// </summary>
public sealed class LiberarVagaOnPedidoCanceladoHandler(
    IVagaOcupadaRepository vagaOcupadaRepository,
    ILogger<LiberarVagaOnPedidoCanceladoHandler> logger)
{
    public async Task HandleAsync(PedidoCanceladoEvent evento, CancellationToken ct = default)
    {
        var liberou = await vagaOcupadaRepository.LiberarPorPedidoAsync(
            evento.PedidoId,
            $"Pedido cancelado: {evento.Motivo}",
            ct);

        if (liberou)
        {
            logger.LogInformation(
                "VagaOcupada liberada pedidoId={PedidoId} motivo={Motivo}",
                evento.PedidoId, evento.Motivo);
        }
        else
        {
            logger.LogDebug(
                "LiberarVaga no-op (vaga já liberada ou inexistente) pedidoId={PedidoId}",
                evento.PedidoId);
        }
    }
}
