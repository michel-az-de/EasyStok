using EasyStock.Application.Services.Notifications.Orchestrators;
using Microsoft.Extensions.Options;

namespace EasyStock.Worker.BackgroundServices;

/// <summary>
/// Wrapper hosted — chama <see cref="INotificacoesColetorOrchestrator"/> a cada
/// <see cref="WorkerOptions.ColetorIntervalSeconds"/>.
/// </summary>
public sealed class ColetorEventosDeEstadoService(
    IServiceProvider serviceProvider,
    IOptions<WorkerOptions> options,
    ILogger<ColetorEventosDeEstadoService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("ColetorEventosDeEstadoService iniciado.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = serviceProvider.CreateScope();
                var orchestrator = scope.ServiceProvider.GetRequiredService<INotificacoesColetorOrchestrator>();
                await orchestrator.ExecutarRodadaAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Erro no ColetorEventosDeEstadoService — continuando próxima rodada.");
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
}
