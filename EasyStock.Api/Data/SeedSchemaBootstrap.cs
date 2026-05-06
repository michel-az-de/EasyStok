using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore;

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
/// A solução é simples: NÃO depender que migrations tenham aplicado tudo certo.
/// SQL idempotente (<c>ADD COLUMN IF NOT EXISTS</c>, <c>CREATE TABLE IF NOT EXISTS</c>)
/// roda no startup da API e como primeira coisa de qualquer seed. Se o schema
/// já existe, é no-op. Se faltava algo, agora está lá.
/// </para>
///
/// <para>
/// Postgres 9.6+ é necessário (Render usa 16+, OK). Cada SQL é uma única
/// statement, então roda fora de transação user-initiated — compatível com
/// <c>NpgsqlRetryingExecutionStrategy</c>.
/// </para>
/// </summary>
public static class SeedSchemaBootstrap
{
    /// <summary>
    /// Garante que <c>Empresas.IsSeedData</c> e a tabela <c>SeedRunLogs</c>
    /// existem. Idempotente — pode ser chamado múltiplas vezes sem efeito.
    /// </summary>
    public static async Task EnsureAsync(
        EasyStockDbContext ctx,
        ILogger logger,
        CancellationToken ct = default)
    {
        try
        {
            // 1. Coluna IsSeedData em Empresas (boolean NOT NULL DEFAULT false).
            //    DEFAULT false cobre rows existentes — não precisa de UPDATE separado.
            await ctx.Database.ExecuteSqlRawAsync(
                @"ALTER TABLE ""Empresas"" ADD COLUMN IF NOT EXISTS ""IsSeedData"" boolean NOT NULL DEFAULT false;",
                ct);

            // 2. Tabela SeedRunLogs (auditoria de runs de seed).
            await ctx.Database.ExecuteSqlRawAsync(@"
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
                );", ct);

            // 3. Registra no __EFMigrationsHistory pra futuras migrations EF
            //    não tentarem recriar (caso alguém regenere a migration limpa).
            await ctx.Database.ExecuteSqlRawAsync(@"
                INSERT INTO ""__EFMigrationsHistory"" (""MigrationId"", ""ProductVersion"")
                VALUES ('20260506233022_AddSeedRunLogAndIsSeedData', '9.0.0')
                ON CONFLICT DO NOTHING;", ct);

            logger.LogInformation("[SeedSchema] Schema verificado: Empresas.IsSeedData + SeedRunLogs OK.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[SeedSchema] Falha ao garantir schema — seed vai falhar até DBA intervir.");
            throw;
        }
    }
}
