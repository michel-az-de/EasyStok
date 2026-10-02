using EasyStock.Api.BackgroundServices;
using EasyStock.Api.Mobile.Services;
using EasyStock.Api.Services.Operacao;
using EasyStock.Api.Services.Storefront;
using EasyStock.Application.Validators;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EasyStock.Api.DependencyInjection;

/// <summary>
/// Agrupa registros que viviam soltos no Program.cs entre "Build" e "DiagnosticoMode":
/// Background Jobs da Application, HttpClient genérico, validators do FluentValidation,
/// Mobile module services (Onda 2-9), ExpirarClienteSessions
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
        services.AddSingleton<OperacaoEventBroker>();
        // S18: a mesma instância alimenta o SSE do console (api/operacao/eventos); troca o no-op da Application.
        services.Replace(ServiceDescriptor.Singleton<
            EasyStock.Application.Ports.Output.Atendimento.IOperacaoEventPublisher, OperacaoEventPublisher>());
        // S60: reserva por SMS do reenvio; desligada sem chave ou com o provedor stub (ReservaSmsConfiguracao).
        services.Replace(ServiceDescriptor.Singleton(EasyStock.Api.Configuration.ReservaSmsConfiguracao.Ler(configuration)));
        // SyncController decomposition: mutation dispatch, auto-link pipeline, reverse pull.
        services.AddScoped<SyncMutationDispatcher>();
        services.AddScoped<SyncAutoLinker>();
        // F8: linkers especializados extraidos do facade SyncAutoLinker.
        services.AddScoped<EasyStock.Api.Mobile.Services.Linkers.CashEntryLinker>();
        services.AddScoped<EasyStock.Api.Mobile.Services.Linkers.ClientLinker>();
        services.AddScoped<EasyStock.Api.Mobile.Services.Linkers.ProductLinker>();
        services.AddScoped<EasyStock.Api.Mobile.Services.Linkers.BatchLinker>();
        services.AddScoped<EasyStock.Api.Mobile.Services.Linkers.OrderLinker>();
        services.AddScoped<SyncReversePullService>();
        // Onda 9: OTA do PWA — lê CACHE_VERSION do sw.js em runtime pra /version reportar
        // a versão real do bundle (sem depender de config drift-prone).
        services.AddSingleton<IPwaVersionProvider, PwaVersionProvider>();

        // Storefront — expirar sessões de clientes (ADR-0012: sliding window 30d).
        services.AddHostedService<ExpirarClienteSessionsBackgroundService>();

        return services;
    }
}
