using EasyStock.Infra.Postgre.Data;
using Microsoft.Extensions.Logging;

namespace EasyStock.Infra.Postgre.Concurrency;

/// <summary>
/// Utilitário para advisory locks do PostgreSQL.
/// Extraído de CobrancaAssinaturaJob para reuso no Worker de notificações.
/// </summary>
public sealed class PostgresAdvisoryLock(
    EasyStockDbContext db,
    ILogger<PostgresAdvisoryLock> logger)
{
    /// <summary>
    /// Tenta adquirir um advisory lock e executa <paramref name="action"/>.
    /// Se o lock não estiver disponível (outra réplica), retorna false sem executar.
    /// O lock é automaticamente liberado ao final.
    /// </summary>
    public async Task<bool> TentarExecutarAsync(long lockKey, Func<CancellationToken, Task> action, CancellationToken ct)
    {
        if (!db.Database.IsNpgsql())
        {
            logger.LogWarning(
                "PostgresAdvisoryLock: provider {Provider} não suporta advisory lock — executando SEM lock.",
                db.Database.ProviderName);
            await action(ct);
            return true;
        }

        await db.Database.OpenConnectionAsync(ct);
        try
        {
            using var tryCmd = db.Database.GetDbConnection().CreateCommand();
            tryCmd.CommandText = "SELECT pg_try_advisory_lock(@key)";
            var p = tryCmd.CreateParameter();
            p.ParameterName = "key";
            p.Value = lockKey;
            tryCmd.Parameters.Add(p);

            var acquired = (bool)(await tryCmd.ExecuteScalarAsync(ct) ?? false);
            if (!acquired)
            {
                logger.LogDebug("Advisory lock {LockKey} já detido por outra réplica — pulando.", lockKey);
                return false;
            }

            try
            {
                await action(ct);
                return true;
            }
            finally
            {
                using var unlockCmd = db.Database.GetDbConnection().CreateCommand();
                unlockCmd.CommandText = "SELECT pg_advisory_unlock(@key)";
                var pu = unlockCmd.CreateParameter();
                pu.ParameterName = "key";
                pu.Value = lockKey;
                unlockCmd.Parameters.Add(pu);
                await unlockCmd.ExecuteScalarAsync(CancellationToken.None);
            }
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }
}
