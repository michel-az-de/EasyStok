using EasyStock.Infra.Postgre.Data;
using Npgsql;

namespace EasyStock.Api.Data;

/// <summary>
/// Bootstrap defensivo de schema necessário pelo módulo de seed.
///
/// <para>
/// Por que existe: migrations EF têm um histórico problemático nesse projeto —
/// migration vazia ficou registrada em <c>__EFMigrationsHistory</c>, schema
/// avançou no snapshot mas SQL nunca rodou, deploys parciais. Resultado: seed
/// quebrava com "column e.IsSeedData does not exist" e similares mesmo após
/// múltiplos deploys.
/// </para>
///
/// <para>
/// A solução definitiva: usar <c>NpgsqlConnection</c> DIRETAMENTE, sem passar
/// pelo EF Core nem pelo <c>NpgsqlRetryingExecutionStrategy</c>. Elimina qualquer
/// interferência de retry strategy, connection state ou transaction tracking do EF.
/// Se o schema já existe, é no-op. Se faltava algo, agora está lá.
/// </para>
/// </summary>
public static class SeedSchemaBootstrap
{
    /// <summary>
    /// Garante que <c>Empresas.IsSeedData</c> e a tabela <c>SeedRunLogs</c>
    /// existem. Idempotente — pode ser chamado múltiplas vezes sem efeito.
    /// Usa Npgsql diretamente (não passa pelo EF) para máxima confiabilidade.
    /// </summary>
    public static async Task EnsureAsync(
        EasyStockDbContext ctx,
        ILogger logger,
        CancellationToken ct = default)
    {
        var connectionString = ctx.Database.GetConnectionString()
            ?? throw new InvalidOperationException("[SeedSchema] Connection string nula — impossível garantir schema.");

        logger.LogInformation("[SeedSchema] Abrindo conexão Npgsql direta para bootstrap de schema…");

        try
        {
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync(ct);

            // Cada statement roda isolada em autocommit — DDL é transacional no Postgres
            // mas sem tx explícita cada statement commita imediatamente e é visível
            // a todas as conexões (sem risco de rollback acidental).

            await ExecAsync(conn, ct,
                @"ALTER TABLE ""Empresas"" ADD COLUMN IF NOT EXISTS ""IsSeedData"" boolean NOT NULL DEFAULT false");

            await ExecAsync(conn, ct, @"
                CREATE TABLE IF NOT EXISTS ""SeedRunLogs"" (
                    ""Id"" uuid NOT NULL,
                    ""AdminEmail"" text NOT NULL,
                    ""TipoSeed"" text NOT NULL,
                    ""Volume"" text NULL,
                    ""StartedAt"" timestamp with time zone NOT NULL,
                    ""CompletedAt"" timestamp with time zone NULL,
                    ""Status"" text NOT NULL,
                    ""EtapasJson"" text NULL,
                    ""BackupJson"" text NULL,
                    ""Erro"" text NULL,
                    ""Resumo"" text NULL,
                    CONSTRAINT ""PK_SeedRunLogs"" PRIMARY KEY (""Id"")
                )");

            await ExecAsync(conn, ct, @"
                INSERT INTO ""__EFMigrationsHistory"" (""MigrationId"", ""ProductVersion"")
                VALUES ('20260506233022_AddSeedRunLogAndIsSeedData', '9.0.0')
                ON CONFLICT DO NOTHING");

            logger.LogInformation("[SeedSchema] Schema OK — Empresas.IsSeedData + SeedRunLogs verificados via Npgsql direto.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[SeedSchema] Falha ao garantir schema via Npgsql direto. Seed vai falhar.");
            throw;
        }
    }

    private static async Task ExecAsync(NpgsqlConnection conn, CancellationToken ct, string sql)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
