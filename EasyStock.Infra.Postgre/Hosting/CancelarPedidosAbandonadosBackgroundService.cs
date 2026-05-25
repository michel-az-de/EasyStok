using EasyStock.Application.Events.Storefront.Handlers;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Domain.Events.Storefront;
using EasyStock.Domain.Sales;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace EasyStock.Infra.Postgre.Hosting;

/// <summary>
/// Background service que cancela pedidos Storefront em <c>AguardandoPagamento</c>
/// há mais de 30 minutos — prevenindo vagas órfãs (ADR-0014 §Solução 2).
///
/// <para>
/// Intervalo: 5 min. Batch máximo: 50. Cada pedido cancelado invoca diretamente
/// <see cref="LiberarVagaOnPedidoCanceladoHandler"/> para liberar a vaga associada.
/// </para>
/// </summary>
public sealed class CancelarPedidosAbandonadosBackgroundService(
    IServiceScopeFactory scopeFactory,
    ILogger<CancelarPedidosAbandonadosBackgroundService> logger) : BackgroundService
{
    private static readonly TimeSpan Intervalo = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan TimeoutAbandonado = TimeSpan.FromMinutes(30);
    private const int MaxBatch = 50;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(Intervalo, stoppingToken).ConfigureAwait(false);

            if (stoppingToken.IsCancellationRequested) break;

            try
            {
                await ProcessarBatchAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "CancelarPedidosAbandonados erro inesperado.");
            }
        }
    }

    private async Task ProcessarBatchAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var pedidoRepo = scope.ServiceProvider.GetRequiredService<IPedidoStorefrontRepository>();
        var vagaRepo = scope.ServiceProvider.GetRequiredService<IVagaOcupadaRepository>();

        var limite = DateTime.UtcNow - TimeoutAbandonado;
        var pedidosExpirados = await pedidoRepo.GetAguardandoPagamentoExpiradosAsync(limite, MaxBatch, ct);

        if (pedidosExpirados.Count == 0) return;

        logger.LogInformation(
            "CancelarPedidosAbandonados processando {Count} pedidos expirados.",
            pedidosExpirados.Count);

        var handler = new LiberarVagaOnPedidoCanceladoHandler(
            vagaRepo,
            scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<LiberarVagaOnPedidoCanceladoHandler>>());

        foreach (var pedido in pedidosExpirados)
        {
            try
            {
                pedido.Status = StatusPedidoMapper.Cancelado;
                pedido.CanceladoEm = DateTime.UtcNow;
                pedido.AlteradoEm = DateTime.UtcNow;
                await pedidoRepo.UpdateAsync(pedido, ct);

                var evento = new PedidoCanceladoEvent(pedido.Id, Guid.Empty, "Timeout 30 min sem pagamento");
                await handler.HandleAsync(evento, ct);

                logger.LogInformation(
                    "CancelarPedidosAbandonados cancelado pedidoId={PedidoId}.", pedido.Id);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex,
                    "CancelarPedidosAbandonados erro ao cancelar pedidoId={PedidoId}.", pedido.Id);
            }
        }
    }
}
