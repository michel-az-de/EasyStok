using EasyStock.Application.Services.Notifications;
using EasyStock.Application.Services.Notifications.Orchestrators;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EasyStock.Infra.Notifications.Hosting;

/// <summary>
/// Wrapper hosted que aguarda <see cref="IOutboxSignaler"/> e delega 1 rodada
/// completa ao <see cref="INotificacoesDispatcherOrchestrator"/>. Reutilizável
/// pelo Worker e pela API quando <see cref="NotificationsHostingMode.Hosted"/>.
/// </summary>
public sealed class DispatcherLoopHostedService(
    INotificacoesDispatcherOrchestrator orchestrator,
    IOutboxSignaler signaler,
    IOptions<NotificationsHostingOptions> options,
    ILogger<DispatcherLoopHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var opts = options.Value;

        logger.LogInformation(
            "DispatcherLoopHostedService iniciado — shards={Shards} batch={Batch}",
            opts.ShardCount, opts.DispatcherBatchSize);

        while (!stoppingToken.IsCancellationRequested)
        {
            await signaler.WaitAsync(stoppingToken);

            try
            {
                await orchestrator.ExecutarRodadaAsync(opts.ShardCount, opts.DispatcherBatchSize, stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Erro no DispatcherLoopHostedService — continuando próxima rodada.");
            }
        }
    }
}
