using EasyStock.Application.Ports.Output.Notifications;
using Microsoft.Extensions.Options;

namespace EasyStock.Worker.BackgroundServices;

/// <summary>
/// Agrega todos os IColetorEventoNotificacao registrados no DI e os executa periodicamente.
/// Cada coletor é responsável por um domínio (produtos vencendo, assinaturas, etc.).
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
                await ExecutarRodadaAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Erro no ColetorEventosDeEstadoService — continuando próxima rodada.");
            }

            await Task.Delay(
                TimeSpan.FromSeconds(options.Value.ColetorIntervalSeconds),
                stoppingToken);
        }
    }

    internal async Task ExecutarRodadaAsync(CancellationToken ct)
    {
        using var scope = serviceProvider.CreateScope();
        var coletores = scope.ServiceProvider
            .GetRequiredService<IEnumerable<IColetorEventoNotificacao>>()
            .ToList();

        if (coletores.Count == 0)
        {
            logger.LogDebug("ColetorEventosDeEstadoService: nenhum coletor registrado.");
            return;
        }

        foreach (var coletor in coletores)
        {
            try
            {
                await coletor.ColetarAsync(ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                logger.LogError(ex, "Erro no coletor {Coletor}.", coletor.GetType().Name);
            }
        }
    }
}
