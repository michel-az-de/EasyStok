using EasyStock.Api.Services;

namespace EasyStock.Api.BackgroundServices;

/// <summary>
/// Job legado para gerar alertas de estoque automaticamente.
/// Mantido por compatibilidade, mas reaproveita o fluxo consolidado de notificações.
/// </summary>
public sealed class AlertasEstoqueJob(
    IServiceScopeFactory scopeFactory,
    ILogger<AlertasEstoqueJob> logger) : BackgroundService
{
    private readonly TimeSpan _interval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Job de alertas de estoque iniciado");

        using var timer = new PeriodicTimer(_interval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await ProcessarAlertasAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Erro no processamento de alertas de estoque");
            }
        }
    }

    private async Task ProcessarAlertasAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var gerador = scope.ServiceProvider.GetRequiredService<GeradorNotificacoesAutomaticas>();
        await gerador.ExecutarAsync(cancellationToken);
    }
}
