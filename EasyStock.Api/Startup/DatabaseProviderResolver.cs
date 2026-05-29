using Npgsql;

namespace EasyStock.Api.Startup;

/// <summary>
/// Resolve qual provedor de banco usar (PostgreSQL ou modo OpenAPI export) com base
/// em config + disponibilidade real do banco. Roda apenas no startup.
///
/// MongoDB foi descontinuado como provedor transacional (ADR 0001) — pedido explícito
/// por Mongo lança <see cref="NotSupportedException"/> com link pro ADR.
///
/// Em produção com provider explicitamente configurado, o caller pula esta resolução
/// (custa 3-5s no cold start) — esta classe só roda em Auto ou Development.
/// </summary>
public static class DatabaseProviderResolver
{
    public static async Task<string> ResolveAsync(
        string configuredProvider,
        string? postgresConnectionString,
        string? mongoConnectionString,
        Serilog.ILogger logger)
    {
        var normalized = configuredProvider.Trim().ToLowerInvariant();

        // OPENAPI_EXPORT=true: Swashbuckle.AspNetCore.Cli precisa do builder DI registrado
        // mas nao toca DB real. Aceita PostgreSQL "imaginario" — DbContext nao chega a abrir
        // conexao (script retorna antes de app.Run() — ver bloco openapi-export ao final).
        var isOpenApiExport = string.Equals(
            Environment.GetEnvironmentVariable("OPENAPI_EXPORT"), "true", StringComparison.OrdinalIgnoreCase);

        if (normalized is "postgres" or "postgresql")
        {
            if (isOpenApiExport) return "postgresql";

            if (!string.IsNullOrWhiteSpace(postgresConnectionString) &&
                await IsPostgresAvailableAsync(postgresConnectionString, logger))
                return "postgresql";

            throw new InvalidOperationException(
                "PostgreSQL configurado mas indisponível. " +
                "Verifique a connection string 'DefaultConnection' e a conectividade com o banco. " +
                "Em dev, suba Postgres via Docker Compose ou aponte para o banco Render dev.");
        }

        if (normalized is "mongodb" or "mongo")
        {
            // B2: Mongo descontinuado como provedor transacional. Falha rápido para
            // operador notar que precisa migrar para Postgres.
            throw new NotSupportedException(
                "MongoDB foi descontinuado como provedor transacional. " +
                "Use Database:Provider=PostgreSQL. Detalhes: docs/adr/0001-mongo-discarded.md.");
        }

        if (normalized is "auto")
        {
            if (isOpenApiExport) return "postgresql";

            if (!string.IsNullOrWhiteSpace(postgresConnectionString) &&
                await IsPostgresAvailableAsync(postgresConnectionString, logger))
            {
                logger.Information("Auto-deteccao: usando PostgreSQL.");
                return "postgresql";
            }

            // Sem fallback: PostgreSQL é o único provedor transacional suportado (#261).
            throw new InvalidOperationException(
                "Auto-deteccao: PostgreSQL indisponível e não há fallback. " +
                "Verifique a connection string 'DefaultConnection' (suba Postgres via Docker Compose em dev).");
        }

        throw new InvalidOperationException($"Database:Provider '{configuredProvider}' não suportado.");
    }

    private static async Task<bool> IsPostgresAvailableAsync(string connectionString, Serilog.ILogger logger)
    {
        try
        {
            var csb = new NpgsqlConnectionStringBuilder(connectionString)
            {
                Timeout = 3,
                CommandTimeout = 3
            };
            await using var conn = new NpgsqlConnection(csb.ToString());
            await conn.OpenAsync();
            return true;
        }
        catch (Exception ex)
        {
            logger.Debug(ex, "PostgreSQL indisponivel.");
            return false;
        }
    }
}
