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
/// Wrapper hosted — chama <see cref="INotificacoesColetorOrchestrator"/> a cada
/// <see cref="NotificationsHostingOptions.ColetorIntervalSeconds"/>.
/// </summary>
public sealed class ColetorLoopHostedService(
    IServiceProvider serviceProvider,
    IOptions<NotificationsHostingOptions> options,
    ILogger<ColetorLoopHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("ColetorLoopHostedService iniciado.");

        while (!stoppingToken.IsCancellationRequested)
        {
            var sw = Stopwatch.StartNew();
            string status = "OK";
            string? detalhe = null;

            try
            {
                using var scope = serviceProvider.CreateScope();
                var orchestrator = scope.ServiceProvider.GetRequiredService<INotificacoesColetorOrchestrator>();
                await orchestrator.ExecutarRodadaAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                status = "Erro";
                detalhe = ex.GetType().Name + ": " + ex.Message;
                logger.LogError(ex, "Erro no ColetorLoopHostedService — continuando próxima rodada.");
            }
            finally
            {
                sw.Stop();
                await GravarHeartbeatAsync("Coletor", status, detalhe,
                    null, (int)sw.ElapsedMilliseconds, stoppingToken);
            }

            try
            {
                await Task.Delay(
                    TimeSpan.FromSeconds(options.Value.ColetorIntervalSeconds),
                    stoppingToken);
            }
            catch (OperationCanceledException) { break; }
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
            logger.LogWarning(ex, "Falha ao gravar heartbeat do Coletor");
        }
    }
}
