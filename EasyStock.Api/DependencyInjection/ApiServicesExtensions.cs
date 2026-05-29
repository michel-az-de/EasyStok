using EasyStock.Api.BackgroundServices;
using EasyStock.Api.Mobile.Services;
using EasyStock.Api.Services.Storefront;
using EasyStock.Application.Validators;
using FluentValidation;

namespace EasyStock.Api.DependencyInjection;

/// <summary>
/// Agrupa registros que viviam soltos no Program.cs entre "Build" e "DiagnosticoMode":
/// Background Jobs da Application, HttpClient genérico, validators do FluentValidation,
/// Mobile module services (Onda 2-9), SeedProgressService e ExpirarClienteSessions
/// (Storefront sliding window).
///
/// DiagnosticoModeService NÃO entra aqui porque depende de <c>diagLevelSwitch</c>
/// (variável local do <c>Program.cs</c> declarada antes do Serilog setup).
///
/// Ordem relativa preservada exatamente como estava no Program.cs.
/// </summary>
public static class ApiServicesExtensions
{
    public static IServiceCollection AddEasyStockApiServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // ── Background Services + misc ────────────────────────────────────────────
        services.AddEasyStockBackgroundJobs(configuration);
        services.AddHttpClient(); // for DiagnosticoInfraController self-testing
        services.AddValidatorsFromAssemblyContaining<CadastrarProdutoCommandValidator>();

        // ── Mobile module services (Onda 2 parte 2: stock reconciliation) ────────
        services.AddScoped<MobileStockReconciler>();
        // Onda 3: vendas mobile -> Venda ERP (Order entregue cria Venda + ItemVenda).
        services.AddScoped<MobileSaleSyncService>();
        // F9-E: resolve Usuario "Sistema Mobile Sync" pra auditoria de produto/movimentacao
        // (tabelas com UsuarioId NOT NULL). Lookup-or-create idempotente por empresa.
        services.AddScoped<MobileSystemUserResolver>();
        // Onda 5: SSE realtime entre devices da mesma loja.
        // Broker é Singleton — listeners persistem cross-request via dictionary in-memory.
        // Em multi-instance, evoluir pra Redis pubsub.
        services.AddSingleton<MobileEventBroker>();
        // SyncController decomposition: mutation dispatch, auto-link pipeline, reverse pull.
        services.AddScoped<SyncMutationDispatcher>();
        services.AddScoped<SyncAutoLinker>();
        services.AddScoped<SyncReversePullService>();
        // Onda 9: OTA do PWA — lê CACHE_VERSION do sw.js em runtime pra /version reportar
        // a versão real do bundle (sem depender de config drift-prone).
        services.AddSingleton<IPwaVersionProvider, PwaVersionProvider>();

        // SeedProgressService: Singleton pra compartilhar estado de runs entre requests.
        // O background job e o polling endpoint falam com a mesma instância.
        services.AddSingleton<SeedProgressService>();

        // Storefront — expirar sessões de clientes (ADR-0012: sliding window 30d).
        services.AddHostedService<ExpirarClienteSessionsBackgroundService>();

        return services;
    }
}
