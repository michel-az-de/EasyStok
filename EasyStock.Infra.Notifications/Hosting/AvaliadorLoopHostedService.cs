using EasyStock.Application.Services.Notifications;
using EasyStock.Application.Services.Notifications.Orchestrators;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EasyStock.Infra.Notifications.Hosting;

/// <summary>
/// Wrapper hosted — chama <see cref="INotificacoesAvaliadorOrchestrator"/> a cada
/// <see cref="NotificationsHostingOptions.AvaliadorIntervalSeconds"/>.
/// Cria scope a cada rodada (orchestrator depende de repos Scoped).
/// </summary>
public sealed class AvaliadorLoopHostedService(
    IServiceProvider serviceProvider,
    IOptions<NotificationsHostingOptions> options,
    ILogger<AvaliadorLoopHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("AvaliadorLoopHostedService iniciado.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = serviceProvider.CreateScope();
                var orchestrator = scope.ServiceProvider.GetRequiredService<INotificacoesAvaliadorOrchestrator>();
                var janela = TimeSpan.FromSeconds(options.Value.AvaliadorIntervalSeconds * 2);
                await orchestrator.ExecutarRodadaAsync(janela, stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Erro no AvaliadorLoopHostedService — continuando próxima rodada.");
            }

            try
            {
                await Task.Delay(
                    TimeSpan.FromSeconds(options.Value.AvaliadorIntervalSeconds),
                    stoppingToken);
            }
            catch (OperationCanceledException) { break; }
        }
    }
}
