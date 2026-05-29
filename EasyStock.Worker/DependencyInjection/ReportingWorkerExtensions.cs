using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Reporting;
using EasyStock.Application.Reporting;
using EasyStock.Application.Reporting.Definitions.EstoquePosicaoAtual;
using EasyStock.Application.Reporting.Definitions.Fiscal.CancelamentosInutilizacoes;
using EasyStock.Application.Reporting.Definitions.Fiscal.LivroSaidas;
using EasyStock.Application.Reporting.Definitions.Fiscal.MapMensal;
using EasyStock.Application.Reporting.Definitions.Fiscal.TotalizadoresFiscais;
using EasyStock.Application.Reporting.Definitions.Fiscal.XmlBulkDownload;
using EasyStock.Application.Reporting.Definitions.VendasPorPeriodo;
using EasyStock.Infra.Async.Reporting;
using EasyStock.Infra.Async.Reporting.Exporters;
using EasyStock.Infra.Async.Reporting.Handlers;
using EasyStock.Infra.Async.Reporting.Handlers.Fiscal;
using EasyStock.Infra.Postgre.Repositories.Reporting;
using EasyStock.Infra.Postgre.Reporting;
using EasyStock.Worker.BackgroundServices;
using Microsoft.AspNetCore.Http;

namespace EasyStock.Worker.DependencyInjection;

/// <summary>
/// Registra todos os serviços do motor de relatórios no contexto do Worker.
/// Sobrescreve <see cref="ICurrentUserAccessor"/> com <see cref="WorkerCurrentUserAccessor"/>
/// para suporte a AsyncLocal tenant context (ADR-R06).
/// </summary>
public static class ReportingWorkerExtensions
{
    public static IServiceCollection AddReportingWorker(this IServiceCollection services)
    {
        // ── ICurrentUserAccessor: override para o Worker (ADR-R06) ──────────────
        // Worker é Generic Host — sem HTTP pipeline. Registra stub nulo de IHttpContextAccessor
        // para que WorkerCurrentUserAccessor saiba que não há HttpContext e use o ReportExecutionContext.
        services.AddSingleton<IHttpContextAccessor, WorkerHttpContextAccessorStub>();
        services.AddScoped<ICurrentUserAccessor, WorkerCurrentUserAccessor>();

        // ── Contexto de execução AsyncLocal ─────────────────────────────────────
        services.AddScoped<IReportExecutionScope, ReportExecutionContext>();

        // ── Defesa em profundidade: tenant-scoped query builder ─────────────────
        services.AddScoped<ITenantScopedQueryBuilder, TenantScopedQueryBuilder>();

        // ── Repositório ──────────────────────────────────────────────────────────
        services.AddScoped<IReportRunRepository, ReportRunRepository>();

        // ── Catálogo de definições (singleton — imutável em runtime) ─────────────
        services.AddSingleton<ReportRegistry>();

        // Definições Tenant — Fase 1
        services.AddSingleton<IReportDefinition, VendasPorPeriodoDefinition>();
        services.AddSingleton<IReportDefinition, EstoquePosicaoAtualDefinition>();

        // Definições Tenant — Fase 2 (NFC-e fiscal)
        services.AddSingleton<IReportDefinition, LivroSaidasDefinition>();
        services.AddSingleton<IReportDefinition, TotalizadoresFiscaisDefinition>();
        services.AddSingleton<IReportDefinition, CancelamentosInutilizacoesDefinition>();
        services.AddSingleton<IReportDefinition, MapMensalDefinition>();
        services.AddSingleton<IReportDefinition, XmlBulkDownloadDefinition>();

        // ── Exporters (singleton — stateless) ───────────────────────────────────
        services.AddSingleton<IReportExporter, CsvExporter>();
        services.AddSingleton<IReportExporter, ExcelExporter>();
        services.AddSingleton<IReportExporter, XmlBulkZipExporter>();

        // ── Resolver de exporters ─────────────────────────────────────────────────
        services.AddSingleton<ReportExporterResolver>();

        // ── Handlers (scoped — cada run tem seu próprio scope) ──────────────────
        services.AddScoped<IReportHandler<VendasPorPeriodoParams, VendasPorPeriodoRow>,
                           VendasPorPeriodoHandler>();
        services.AddScoped<IReportHandler<EstoquePosicaoAtualParams, EstoquePosicaoAtualRow>,
                           EstoquePosicaoAtualHandler>();

        // Handlers Tenant — Fase 2 (NFC-e fiscal)
        services.AddScoped<IReportHandler<LivroSaidasParams,                LivroSaidasRow>,
                           LivroSaidasHandler>();
        services.AddScoped<IReportHandler<TotalizadoresFiscaisParams,       TotalizadoresFiscaisRow>,
                           TotalizadoresFiscaisHandler>();
        services.AddScoped<IReportHandler<CancelamentosInutilizacoesParams, CancelamentosInutilizacoesRow>,
                           CancelamentosInutilizacoesHandler>();
        services.AddScoped<IReportHandler<MapMensalParams,                  MapMensalRow>,
                           MapMensalHandler>();
        services.AddScoped<IReportHandler<XmlBulkDownloadParams,            XmlBulkDownloadRow>,
                           XmlBulkDownloadHandler>();

        // ── Métricas (Singleton — IMeterFactory resolve internamente) ────────────
        services.AddSingleton<ReportingMetricsService>();

        // ── Background services ──────────────────────────────────────────────────
        services.AddHostedService<ReportRunnerBackgroundService>();
        services.AddHostedService<ReportWatchdogBackgroundService>();

        return services;
    }
}

/// <summary>
/// Stub de <see cref="IHttpContextAccessor"/> para o Worker (Generic Host).
/// Sempre retorna <see langword="null"/> — o Worker não tem pipeline HTTP.
/// O <see cref="WorkerCurrentUserAccessor"/> trata HttpContext nulo lendo do
/// <see cref="IReportExecutionScope"/> (ADR-R06).
/// </summary>
internal sealed class WorkerHttpContextAccessorStub : IHttpContextAccessor
{
    public HttpContext? HttpContext { get => null; set { } }
}
