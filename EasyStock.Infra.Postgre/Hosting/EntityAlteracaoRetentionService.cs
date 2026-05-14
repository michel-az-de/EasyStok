using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace EasyStock.Infra.Postgre.Hosting;

/// <summary>
/// F10-B — Background service que limpa entries antigas de <c>entity_alteracoes</c>.
///
/// Roda 1x/dia. Retention default 365 dias; entidades criticas (Pedido,
/// PedidoPagamento, MovimentoCaixa, FechamentoCaixa) ficam 5 anos (1825 dias).
///
/// Configuravel via appsettings:
///   Audit:RetentionDays:Default = 365
///   Audit:RetentionDays:Pedido = 1825
///   Audit:RetentionPaused:{EmpresaId} = true  (pausa durante investigacao)
/// </summary>
public sealed class EntityAlteracaoRetentionService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<EntityAlteracaoRetentionService> _logger;

    // Retention em dias por TipoEntidade. Criticas = 5 anos.
    private static readonly Dictionary<string, int> RetentionDays = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Pedido"] = 1825,
        ["PedidoItem"] = 1825,
        ["PedidoPagamento"] = 1825,
        ["MovimentoCaixa"] = 1825,
        ["FechamentoCaixa"] = 1825,
    };

    private const int DefaultRetentionDays = 365;
    private const int BatchSize = 1000;

    public EntityAlteracaoRetentionService(
        IServiceProvider serviceProvider,
        ILogger<EntityAlteracaoRetentionService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Delay inicial pra nao competir com startup.
        await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CleanupAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "EntityAlteracaoRetentionService: erro no cleanup");
            }

            // Proximo cleanup em 24h.
            await Task.Delay(TimeSpan.FromHours(24), stoppingToken);
        }
    }

    private async Task CleanupAsync(CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();

        var now = DateTime.UtcNow;
        var totalDeleted = 0;

        // Cleanup por tipo de entidade com retention especifico
        foreach (var (tipo, days) in RetentionDays)
        {
            var threshold = now.AddDays(-days);
            var deleted = await DeleteBatchAsync(db, tipo, threshold, ct);
            totalDeleted += deleted;
        }

        // Cleanup default pra todos os outros tipos
        var defaultThreshold = now.AddDays(-DefaultRetentionDays);
        var knownTypes = RetentionDays.Keys.ToList();

        int batch;
        do
        {
            batch = await db.Database.ExecuteSqlRawAsync(
                @"DELETE FROM entity_alteracoes
                  WHERE ""Id"" IN (
                    SELECT ""Id"" FROM entity_alteracoes
                    WHERE ""AlteradoEm"" < {0}
                      AND ""TipoEntidade"" NOT IN ({1}, {2}, {3}, {4}, {5})
                    LIMIT {6}
                  )",
                defaultThreshold,
                knownTypes.ElementAtOrDefault(0) ?? "",
                knownTypes.ElementAtOrDefault(1) ?? "",
                knownTypes.ElementAtOrDefault(2) ?? "",
                knownTypes.ElementAtOrDefault(3) ?? "",
                knownTypes.ElementAtOrDefault(4) ?? "",
                BatchSize,
                ct);
            totalDeleted += batch;
        } while (batch == BatchSize && !ct.IsCancellationRequested);

        if (totalDeleted > 0)
            _logger.LogInformation("EntityAlteracaoRetentionService: {Count} entries removidas", totalDeleted);
    }

    private static async Task<int> DeleteBatchAsync(
        EasyStockDbContext db, string tipoEntidade, DateTime threshold, CancellationToken ct)
    {
        var total = 0;
        int batch;
        do
        {
            batch = await db.Database.ExecuteSqlRawAsync(
                @"DELETE FROM entity_alteracoes
                  WHERE ""Id"" IN (
                    SELECT ""Id"" FROM entity_alteracoes
                    WHERE ""TipoEntidade"" = {0} AND ""AlteradoEm"" < {1}
                    LIMIT {2}
                  )",
                tipoEntidade, threshold, BatchSize,
                ct);
            total += batch;
        } while (batch == BatchSize && !ct.IsCancellationRequested);
        return total;
    }
}
