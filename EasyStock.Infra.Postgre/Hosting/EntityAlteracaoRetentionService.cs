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

        // EF translates List<string>.Contains() to SQL NOT IN
        var defaultDeleted = await db.EntityAlteracoes
            .Where(a => a.AlteradoEm < defaultThreshold
                     && !knownTypes.Contains(a.TipoEntidade))
            .OrderBy(a => a.AlteradoEm)
            .Take(BatchSize)
            .ExecuteDeleteAsync(ct);
        totalDeleted += defaultDeleted;

        // Continue in batches if we hit the limit
        while (defaultDeleted == BatchSize && !ct.IsCancellationRequested)
        {
            defaultDeleted = await db.EntityAlteracoes
                .Where(a => a.AlteradoEm < defaultThreshold
                         && !knownTypes.Contains(a.TipoEntidade))
                .OrderBy(a => a.AlteradoEm)
                .Take(BatchSize)
                .ExecuteDeleteAsync(ct);
            totalDeleted += defaultDeleted;
        }

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
