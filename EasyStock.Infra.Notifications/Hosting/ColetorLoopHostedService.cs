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
            try
            {
                using var scope = serviceProvider.CreateScope();
                var orchestrator = scope.ServiceProvider.GetRequiredService<INotificacoesColetorOrchestrator>();
                await orchestrator.ExecutarRodadaAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Erro no ColetorLoopHostedService — continuando próxima rodada.");
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
