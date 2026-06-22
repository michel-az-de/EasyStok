using EasyStock.Domain.Entities.Storefront;

namespace EasyStock.Api.Services.Storefront;

/// <summary>
/// Background service que remove sessões de clientes storefront expiradas (ADR-0012).
///
/// <para>
/// Executa 1×/hora e deleta <see cref="ClienteSession"/> onde
/// <c>UltimoUsoEm &lt; agora - 30 dias</c> OU <c>Revogada = true</c>.
/// Mantém a tabela <c>ClienteSessions</c> enxuta sem acúmulo de registros mortos.
/// </para>
/// </summary>
public class ExpirarClienteSessionsBackgroundService(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<ExpirarClienteSessionsBackgroundService> logger) : BackgroundService
{
    private static readonly TimeSpan Intervalo = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("ExpirarClienteSessions background service iniciado.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ExpirarAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Erro ao expirar sessões de clientes storefront.");
            }

            await Task.Delay(Intervalo, stoppingToken).ContinueWith(_ => { }, CancellationToken.None);
        }

        logger.LogInformation("ExpirarClienteSessions background service encerrado.");
    }

    private async Task ExpirarAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EasyStock.Infra.Postgre.Data.EasyStockDbContext>();

        var limite = timeProvider.GetUtcNow().UtcDateTime - ClienteSession.SlidingWindow;

        var expiradas = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
            .ToListAsync(
                db.Set<ClienteSession>().Where(s => s.Revogada || s.UltimoUsoEm < limite),
                ct);

        if (expiradas.Count == 0)
            return;

        db.Set<ClienteSession>().RemoveRange(expiradas);
        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Sessões storefront expiradas removidas: count={Count} limite={Limite:O}",
            expiradas.Count, limite);
    }
}
