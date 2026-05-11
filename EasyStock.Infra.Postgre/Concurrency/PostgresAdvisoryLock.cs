using EasyStock.Infra.Postgre.Data;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace EasyStock.Infra.Postgre.Concurrency;

/// <summary>
/// Utilitário para advisory locks do PostgreSQL.
/// <para>
/// <b>Conexão dedicada (raw NpgsqlConnection)</b>: o lock vive na sessão da conexão.
/// Se reusássemos <c>db.Database.GetDbConnection()</c>, o <c>EnableRetryOnFailure</c>
/// configurado em <c>AddEasyStockPostgreInfrastructure</c> podia abrir uma nova
/// conexão no meio de <c>action()</c> e a nova sessão NÃO teria o lock — outra
/// réplica conseguiria adquirir paralelamente. Aqui usamos uma <c>NpgsqlConnection</c>
/// fora do EF Core (sem retry policy) e mantemos ela aberta enquanto a action roda;
/// se a sessão cair, <c>action()</c> recebe a exception e nada continua "achando que
/// tem lock".
/// </para>
/// <para>
/// O <c>action()</c> continua usando o <see cref="EasyStockDbContext"/> injetado normalmente
/// — duas conexões coexistem: uma só pro lock, outra pra trabalho.
/// </para>
/// </summary>
public sealed class PostgresAdvisoryLock(
    EasyStockDbContext db,
    IConfiguration configuration,
    ILogger<PostgresAdvisoryLock> logger)
{
    /// <summary>
    /// Tenta adquirir um advisory lock e executa <paramref name="action"/>.
    /// Se o lock não estiver disponível (outra réplica), retorna false sem executar.
    /// O lock é automaticamente liberado ao final (unlock explícito + dispose da conexão).
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

        var connStr = configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(connStr))
        {
            logger.LogWarning(
                "PostgresAdvisoryLock: ConnectionStrings:DefaultConnection ausente — executando SEM lock.");
            await action(ct);
            return true;
        }

        await using var lockConn = new NpgsqlConnection(connStr);
        await lockConn.OpenAsync(ct);

        bool acquired;
        await using (var tryCmd = lockConn.CreateCommand())
        {
            tryCmd.CommandText = "SELECT pg_try_advisory_lock(@key)";
            var p = tryCmd.CreateParameter();
            p.ParameterName = "key";
            p.Value = lockKey;
            tryCmd.Parameters.Add(p);
            acquired = (bool)(await tryCmd.ExecuteScalarAsync(ct) ?? false);
        }

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
            try
            {
                await using var unlockCmd = lockConn.CreateCommand();
                unlockCmd.CommandText = "SELECT pg_advisory_unlock(@key)";
                var pu = unlockCmd.CreateParameter();
                pu.ParameterName = "key";
                pu.Value = lockKey;
                unlockCmd.Parameters.Add(pu);
                await unlockCmd.ExecuteScalarAsync(CancellationToken.None);
            }
            catch (Exception ex)
            {
                // Falha no unlock explícito não é fatal: dispose da conexão fecha
                // a sessão e o Postgres libera automaticamente todos os locks dela.
                logger.LogWarning(ex,
                    "Falha ao executar pg_advisory_unlock({LockKey}) — dispose da conexão libera o lock.",
                    lockKey);
            }
        }
    }
}
