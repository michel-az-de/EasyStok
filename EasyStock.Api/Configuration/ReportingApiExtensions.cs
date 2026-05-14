using EasyStock.Application.Ports.Output.Reporting;
using EasyStock.Application.Reporting;
using EasyStock.Application.Reporting.Definitions.Admin.Faturamento;
using EasyStock.Application.Reporting.Definitions.Admin.Tenants;
using EasyStock.Application.Reporting.Definitions.Admin.Tickets;
using EasyStock.Application.Reporting.Definitions.EstoquePosicaoAtual;
using EasyStock.Application.Reporting.Definitions.Fiscal.CancelamentosInutilizacoes;
using EasyStock.Application.Reporting.Definitions.Fiscal.LivroSaidas;
using EasyStock.Application.Reporting.Definitions.Fiscal.MapMensal;
using EasyStock.Application.Reporting.Definitions.Fiscal.TotalizadoresFiscais;
using EasyStock.Application.Reporting.Definitions.Fiscal.XmlBulkDownload;
using EasyStock.Application.Reporting.Definitions.VendasPorPeriodo;
using EasyStock.Application.UseCases.Reports;
using EasyStock.Infra.Async.Reporting.QuickReports;
using EasyStock.Infra.Async.Reporting;
using EasyStock.Infra.Async.Reporting.Handlers;
using EasyStock.Infra.Async.Reporting.Handlers.Admin;
using EasyStock.Infra.Async.Reporting.Handlers.Fiscal;
using EasyStock.Infra.Postgre.Repositories.Reporting;
using EasyStock.Infra.Postgre.Reporting;
using Microsoft.Extensions.DependencyInjection;

namespace EasyStock.Api.Configuration;

/// <summary>
/// Registra todos os serviços do módulo de Relatórios no contexto da API.
/// Nota: <see cref="Application.Ports.Output.ICurrentUserAccessor"/> e
/// <see cref="Application.Ports.Output.Storage.IFileStorage"/> já são registrados
/// em <see cref="ApiServiceCollectionExtensions"/>; não duplicar aqui.
/// A policy "RequireSuperAdmin" é registrada em AddEasyStockAuth.
/// </summary>
public static class ReportingApiExtensions
{
    public static IServiceCollection AddReportingApi(this IServiceCollection services)
    {
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

        // Definições Admin SaaS — Fase 1b (ADR-R12: contexto AdminSaaS, cross-tenant)
        services.AddSingleton<IReportDefinition, MrrArrChurnDefinition>();
        services.AddSingleton<IReportDefinition, InadimplenciaDefinition>();
        services.AddSingleton<IReportDefinition, SlaVioladoDefinition>();
        services.AddSingleton<IReportDefinition, CsatMensalDefinition>();
        services.AddSingleton<IReportDefinition, TenantsUsoDefinition>();

        // ── Handlers de relatório (Scoped — cada request tem seu EF context) ─────
        services.AddScoped<IReportHandler<VendasPorPeriodoParams,    VendasPorPeriodoRow>,    VendasPorPeriodoHandler>();
        services.AddScoped<IReportHandler<EstoquePosicaoAtualParams, EstoquePosicaoAtualRow>, EstoquePosicaoAtualHandler>();

        // Handlers Tenant — Fase 2 (NFC-e fiscal)
        services.AddScoped<IReportHandler<LivroSaidasParams,                  LivroSaidasRow>,                  LivroSaidasHandler>();
        services.AddScoped<IReportHandler<TotalizadoresFiscaisParams,         TotalizadoresFiscaisRow>,         TotalizadoresFiscaisHandler>();
        services.AddScoped<IReportHandler<CancelamentosInutilizacoesParams,   CancelamentosInutilizacoesRow>,   CancelamentosInutilizacoesHandler>();
        services.AddScoped<IReportHandler<MapMensalParams,                    MapMensalRow>,                    MapMensalHandler>();
        services.AddScoped<IReportHandler<XmlBulkDownloadParams,              XmlBulkDownloadRow>,              XmlBulkDownloadHandler>();

        // Handlers Admin SaaS (Scoped — usam IgnoreQueryFilters() diretamente, ADR-R07 satisfeito por bypass intencional)
        services.AddScoped<IReportHandler<MrrArrChurnParams,  MrrArrChurnRow>,  MrrArrChurnHandler>();
        services.AddScoped<IReportHandler<InadimplenciaParams, InadimplenciaRow>, InadimplenciaHandler>();
        services.AddScoped<IReportHandler<SlaVioladoParams,   SlaVioladoRow>,   SlaVioladoHandler>();
        services.AddScoped<IReportHandler<CsatMensalParams,   CsatMensalRow>,   CsatMensalHandler>();
        services.AddScoped<IReportHandler<TenantsUsoParams,   TenantsUsoRow>,   TenantsUsoHandler>();

        // ── Contexto de execução (AsyncLocal + defesa multi-tenant) ──────────────
        services.AddScoped<IReportExecutionScope, ReportExecutionContext>();
        services.AddScoped<ITenantScopedQueryBuilder, TenantScopedQueryBuilder>();

        // ── Repositório ──────────────────────────────────────────────────────────
        services.AddScoped<IReportRunRepository, ReportRunRepository>();

        // ── Métricas (Singleton — IMeterFactory resolve internamente) ────────────
        services.AddSingleton<ReportingMetricsService>();

        // ── UseCases ─────────────────────────────────────────────────────────────
        services.AddScoped<EnqueueReportRunUseCase>();
        services.AddScoped<GetReportRunUseCase>();
        services.AddScoped<ListMyReportRunsUseCase>();
        services.AddScoped<CancelReportRunUseCase>();
        services.AddScoped<ListReportCatalogUseCase>();
        services.AddScoped<GetReportSchemaUseCase>();
        services.AddScoped<PreviewReportUseCase>();
        services.AddScoped<GetReportDataUseCase>();

        // ── Quick Reports (mobile/PWA — síncronos, sem fila, §27.7) ─────────────
        services.AddScoped<GetVendasHojeQuery>();
        services.AddScoped<GetCaixaTurnoQuery>();
        services.AddScoped<GetEstoqueBuscaQuery>();
        services.AddScoped<GetNfceHojeQuery>();
        services.AddScoped<GetVendasVendedorTurnoQuery>();

        return services;
    }
}
