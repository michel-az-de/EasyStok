using System.Diagnostics;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Services.Notifications;
using EasyStock.Application.Services.Notifications.Orchestrators;
using Microsoft.Extensions.DependencyInjection;
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
    IServiceProvider serviceProvider,
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

            var sw = Stopwatch.StartNew();
            int processados = 0;
            string status = "OK";
            string? detalhe = null;

            try
            {
                processados = await orchestrator.ExecutarRodadaAsync(
                    opts.ShardCount, opts.DispatcherBatchSize, stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                status = "Erro";
                detalhe = ex.GetType().Name + ": " + ex.Message;
                logger.LogError(ex, "Erro no DispatcherLoopHostedService — continuando próxima rodada.");
            }
            finally
            {
                sw.Stop();
                await GravarHeartbeatAsync("Dispatcher", status, detalhe,
                    processados, (int)sw.ElapsedMilliseconds, stoppingToken);
            }
        }
    }

    private async Task GravarHeartbeatAsync(
        string servico, string status, string? detalhe,
        int? itensProcessados, int? duracaoMs, CancellationToken ct)
    {
        try
        {
            using var scope = serviceProvider.CreateScope();
            var recorder = scope.ServiceProvider.GetRequiredService<IHeartbeatRecorder>();
            await recorder.RecordAsync(servico, status, detalhe, itensProcessados, duracaoMs, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Falha ao gravar heartbeat do Dispatcher");
        }
    }
}
