using EasyStock.Api.Observability;
using EasyStock.Api.Observability.HealthChecks;
using EasyStock.Application.Ports.Output.Fiscal;
using EasyStock.Infra.Integrations.DependencyInjection;
using EasyStock.Infra.Integrations.Fiscal;
using EasyStock.Infra.Integrations.Fiscal.FocusNFe.DependencyInjection;
using EasyStock.Infra.Integrations.Fiscal.Mock.DependencyInjection;
using EasyStock.Infra.Notifications.Hosting;
using EasyStock.Infra.Postgre.DependencyInjection;
using Serilog;

namespace EasyStock.Api.Startup;

/// <summary>
/// Resolve qual banco usar (PostgreSQL é o único transacional suportado — ADR 0001)
/// e registra todos os services dependentes: EF Core, repos, health checks, módulo
/// Fiscal NFC-e (Polly + Focus NFe + Mock + Cert A1), DataProtection.
///
/// Em produção com provider explicitamente "PostgreSQL", pula a checagem de auto-detect
/// (custa 3-5s no cold start). MongoDB lança <see cref="NotSupportedException"/>.
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
        string? postgresConnectionString,
        string? mongoConnectionString)
    {
        string resolvedProvider;
        if (builder.Environment.IsProduction() &&
            !databaseProvider.Trim().Equals("Auto", StringComparison.OrdinalIgnoreCase))
        {
            resolvedProvider = databaseProvider.Trim().ToLowerInvariant() switch
            {
                "postgres" or "postgresql" => "postgresql",
                "mongodb" or "mongo" => "mongodb",
                _ => "postgresql"
            };
        }
        else
        {
            resolvedProvider = await DatabaseProviderResolver.ResolveAsync(
                databaseProvider, postgresConnectionString, mongoConnectionString, Log.Logger);
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
            case "mongodb":
                // MongoDB foi descontinuado como provedor transacional (B2 do plano de a��o).
                // Paridade incompleta com Postgres (sem Venda, ItemVenda, MovimentacaoEstoque,
                // Caixa, Lote, Pedido) gerava risco de bug silencioso. Postgres � o �nico
                // provedor transacional suportado. Rever ADR 0001-mongo-discarded.
                throw new NotSupportedException(
                    "MongoDB foi descontinuado como provedor transacional. " +
                    "Use Database:Provider=PostgreSQL. Detalhes: docs/adr/0001-mongo-discarded.md.");

            case "postgresql":
                builder.Services.AddEasyStockPostgreInfrastructure(postgresConnectionString!, builder.Configuration);
                builder.Services.AddHealthChecks()
                    .AddNpgSql(postgresConnectionString!, name: "PostgreSQL", tags: ["ready", "api"])
                    .AddCheck<RedisHealthCheck>("Redis", tags: ["api"])           // sem tag "ready" — Redis degradado não remove pod do LB
                    .AddCheck<ConfigurationHealthCheck>("Configuracao", tags: ["ready", "api"])
                    .AddNotificationsHosting();
                // Modulo Fiscal NFC-e (F2) — Polly pipelines + adapters Focus NFe + Mock + cert A1
                builder.Services.AddEasyStockIntegrationResilience();
                builder.Services.AddFocusNFeAdapter(builder.Configuration);
                builder.Services.AddMockFiscalGateway();
                // Scoped (não Singleton): a factory consome IEnumerable<IGatewayFiscal>, e os
                // adapters (Focus/Mock) são Scoped (dependem de serviços scoped como
                // INfeCertificadoA1Service). Como Singleton, capturava gateways scoped
                // (captive dependency / lifetime mismatch) — só não explodia em prod porque
                // ValidateOnBuild fica off lá. Os consumidores (use cases fiscais) são Scoped.
                builder.Services.AddScoped<IGatewayFiscalFactory, GatewayFiscalFactory>();
                builder.Services.AddDataProtection();
                break;

            default:
                throw new InvalidOperationException($"Database:Provider '{databaseProvider}' não suportado.");
        }

        return (resolvedProvider, infraState);
    }
}
