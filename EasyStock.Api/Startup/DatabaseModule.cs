using EasyStock.Api.Observability;
using EasyStock.Api.Observability.HealthChecks;
using EasyStock.Infra.Integrations.DependencyInjection;
using EasyStock.Infra.Notifications.Hosting;
using EasyStock.Infra.Postgre.DependencyInjection;
using Serilog;

namespace EasyStock.Api.Startup;

/// <summary>
/// Resolve qual banco usar (PostgreSQL é o único transacional suportado — ADR 0001)
/// e registra todos os services dependentes: EF Core, repos, health checks, Polly,
/// DataProtection.
///
/// Em produção com provider explicitamente "PostgreSQL", pula a checagem de auto-detect
/// (custa 3-5s no cold start).
///
/// Retorna o provider resolvido + <see cref="ResolvedInfrastructureState"/> (singleton
/// já registrado no DI) — caller usa os dois pra logging, gates de seed/migration e
/// rastreamento de erro.
/// </summary>
public static class DatabaseModule
{
    public static async Task<(string resolvedProvider, ResolvedInfrastructureState infraState)> ConfigureAsync(
        WebApplicationBuilder builder,
        string databaseProvider,
        string? postgresConnectionString)
    {
        string resolvedProvider;
        if (builder.Environment.IsProduction() &&
            !databaseProvider.Trim().Equals("Auto", StringComparison.OrdinalIgnoreCase))
        {
            resolvedProvider = databaseProvider.Trim().ToLowerInvariant() switch
            {
                "postgres" or "postgresql" => "postgresql",
                _ => "postgresql"
            };
        }
        else
        {
            resolvedProvider = await DatabaseProviderResolver.ResolveAsync(
                databaseProvider, postgresConnectionString, Log.Logger);
        }

        // PostgreSQL é o único provedor suportado (#261) — não há mais fallback runtime.
        var infraState = new ResolvedInfrastructureState
        {
            DatabaseProvider = resolvedProvider,
            ConfiguredProvider = databaseProvider,
            IsFallback = false,
            StartupTime = DateTimeOffset.UtcNow,
            Environment = builder.Environment.EnvironmentName
        };
        builder.Services.AddSingleton(infraState);

        switch (resolvedProvider)
        {
            case "postgresql":
                builder.Services.AddEasyStockPostgreInfrastructure(postgresConnectionString!, builder.Configuration);
                builder.Services.AddHealthChecks()
                    .AddNpgSql(postgresConnectionString!, name: "PostgreSQL", tags: ["ready", "api"])
                    .AddCheck<RedisHealthCheck>("Redis", tags: ["api"])           // sem tag "ready" — Redis degradado não remove pod do LB
                    .AddCheck<ConfigurationHealthCheck>("Configuracao", tags: ["ready", "api"])
                    .AddNotificationsHosting();
                // Polly pipelines compartilhados pelas integracoes HTTP
                builder.Services.AddEasyStockIntegrationResilience();
                // Atendimento WhatsApp (S02) — cliente da Cloud API que MetaCloudWhatsAppProvider delega.
                builder.Services.AddEasyStockWhatsAppCloudClient(builder.Configuration);
                builder.Services.AddEasyStockMetaMensageria(builder.Configuration);
                // Atendimento WhatsApp (S06) — LLM do agente (Anthropic:*; desligado sem chave).
                builder.Services.AddEasyStockAgenteLlm(builder.Configuration);
                // Login com Google (#1324): Auth:Google:ClientId; desligado sem ele.
                builder.Services.Configure<EasyStock.Infra.Integrations.Auth.GoogleAuthOptions>(
                    builder.Configuration.GetSection(EasyStock.Infra.Integrations.Auth.GoogleAuthOptions.Secao));
                builder.Services.AddSingleton<EasyStock.Application.Ports.Output.Auth.IGoogleIdTokenValidator,
                    EasyStock.Infra.Integrations.Auth.GoogleIdTokenValidator>();
                // Key ring compartilhado com o Worker via Postgres (#1035). O certificado A1
                // cifrado aqui e decifrado la na emissao/reprocessamento fiscal — com o
                // registro default cada processo tinha o seu key ring e o Unprotect cruzado
                // era impossivel.
                builder.Services.AddEasyStockDataProtection();
                break;

            default:
                throw new InvalidOperationException($"Database:Provider '{databaseProvider}' não suportado.");
        }

        return (resolvedProvider, infraState);
    }
}
