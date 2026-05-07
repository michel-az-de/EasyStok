using EasyStock.Application.Services.Notifications.Orchestrators;
using Microsoft.Extensions.Options;

namespace EasyStock.Worker.BackgroundServices;

/// <summary>
/// Wrapper hosted que aguarda sinal de wakeup (LISTEN/NOTIFY ou polling) e delega
/// a 1 rodada completa ao <see cref="INotificacoesDispatcherOrchestrator"/>.
/// Toda a lógica de despacho/retry/fallback vive no orchestrator (Infra.Postgre).
/// </summary>
public sealed class DispatcherOutboxService(
    INotificacoesDispatcherOrchestrator orchestrator,
    IOptions<WorkerOptions> options,
    ILogger<DispatcherOutboxService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var opts = options.Value;

        logger.LogInformation(
            "DispatcherOutboxService iniciado — shards={Shards} batch={Batch}",
            opts.ShardCount, opts.DispatcherBatchSize);

        while (!stoppingToken.IsCancellationRequested)
        {
            // Aguarda sinal de LISTEN/NOTIFY ou timeout de polling
            await OutboxListenService.NotifySignal.WaitAsync(stoppingToken);

            try
            {
                await orchestrator.ExecutarRodadaAsync(opts.ShardCount, opts.DispatcherBatchSize, stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Erro no DispatcherOutboxService — continuando próxima rodada.");
            }
        }
    }
}
