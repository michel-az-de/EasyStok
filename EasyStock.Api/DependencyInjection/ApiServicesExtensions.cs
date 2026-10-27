using EasyStock.Api.BackgroundServices;
using EasyStock.Api.Mobile.Services;
using EasyStock.Api.Services.Operacao;
using EasyStock.Api.Services.Storefront;
using EasyStock.Application.Validators;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EasyStock.Api.DependencyInjection;

/// <summary>
/// Agrupa registros que viviam soltos no Program.cs:
/// Background Jobs da Application, HttpClient genérico, validators do FluentValidation,
/// Mobile module services (Onda 2-9), ExpirarClienteSessions
/// (Storefront sliding window).
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
        services.AddHttpClient();
        services.AddScoped<EasyStock.Application.UseCases.Atendimento.Entregas.LogisticaUseCases>();
        services.AddScoped<EasyStock.Api.Data.DemoStore.LogisticaDemoSeeder>();
        services.AddSingleton<RastreioLogisticaLink>();
        var empresaDemo = Guid.TryParse(configuration["Logistica:EmpresaDemonstracaoId"], out var demoId) ? demoId : Guid.Empty;
        services.AddSingleton(new EasyStock.Application.UseCases.Atendimento.Entregas.ConfiguracaoLogistica(empresaDemo,
            new EasyStock.Application.Ports.Output.Integrations.FreteEndereco("Cozinha de demonstração",
                "Origem sintética de teste, São Paulo", "+5500000000000", -23.55, -46.63)));
        // Exceção aprovada para #1693: apenas empresa demo explícita. Nunca fallback de credencial.
        if (empresaDemo != Guid.Empty)
        {
            services.AddSingleton<EasyStock.Application.Ports.Output.Integrations.IFreteProvider>(
                _ => new EasyStock.Infra.Integrations.Entregas.SimuladorFreteProvider("uber_direct"));
            services.AddSingleton<EasyStock.Application.Ports.Output.Integrations.IFreteProvider>(
                _ => new EasyStock.Infra.Integrations.Entregas.SimuladorFreteProvider("lalamove"));
        }
        services.AddValidatorsFromAssemblyContaining<CadastrarProdutoCommandValidator>();

        // ── Mobile module services (Onda 2 parte 2: stock reconciliation) ────────
        services.AddScoped<MobileStockReconciler>();
        services.AddScoped<LoteMobileEstadoReconciler>();
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
        services.AddScoped<LegacySnapshotImportService>();
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
