using EasyStock.Application.Services.Notifications.Orchestrators;
using Microsoft.Extensions.Options;

namespace EasyStock.Worker.BackgroundServices;

/// <summary>
/// Wrapper hosted — chama <see cref="INotificacoesAvaliadorOrchestrator"/> a cada
/// <see cref="WorkerOptions.AvaliadoresIntervalSeconds"/>.
/// </summary>
public sealed class AvaliadorRotinasService(
    IServiceProvider serviceProvider,
    IOptions<WorkerOptions> options,
    ILogger<AvaliadorRotinasService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("AvaliadorRotinasService iniciado.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = serviceProvider.CreateScope();
                var orchestrator = scope.ServiceProvider.GetRequiredService<INotificacoesAvaliadorOrchestrator>();
                var janela = TimeSpan.FromSeconds(options.Value.AvaliadoresIntervalSeconds * 2);
                await orchestrator.ExecutarRodadaAsync(janela, stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Erro no AvaliadorRotinasService — continuando próxima rodada.");
            }

            try
            {
                await Task.Delay(
                    TimeSpan.FromSeconds(options.Value.AvaliadoresIntervalSeconds),
                    stoppingToken);
            }
            catch (OperationCanceledException) { break; }
        }
    }
}
