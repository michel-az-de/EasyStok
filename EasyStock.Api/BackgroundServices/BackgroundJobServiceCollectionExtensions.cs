using EasyStock.Api.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EasyStock.Api.BackgroundServices;

public static class BackgroundJobServiceCollectionExtensions
{
    public static IServiceCollection AddEasyStockBackgroundJobs(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<BackgroundJobOptions>(
            configuration.GetSection(BackgroundJobOptions.SectionName));

        services.TryAddSingleton<IPedidoFornecedorRecebimentoProcessor, NoOpPedidoFornecedorRecebimentoProcessor>();

        var options = configuration.GetSection(BackgroundJobOptions.SectionName).Get<BackgroundJobOptions>()
            ?? new BackgroundJobOptions();

        if (options.EnableAnalisadorEstoque)
            services.AddHostedService<AnalisadorEstoqueBackgroundService>();

        if (options.EnableCacheWarmup)
            services.AddHostedService<CacheWarmupService>();

        // Health snapshot service (singleton para ser injetado no DiagnosticoController).
        // O singleton sempre é registrado para o DiagnosticoController poder consumir o último snapshot,
        // mas o HostedService (loop em background) só roda se a flag estiver habilitada.
        services.AddSingleton<HealthSnapshotService>();
        if (options.EnableHealthSnapshot)
            services.AddHostedService(sp => sp.GetRequiredService<HealthSnapshotService>());

        // Backup automático de logs no file storage (a cada 30 min)
        if (options.EnableLogStorage)
            services.AddHostedService<LogStorageBackgroundService>();

        if (options.EnableAlertasEstoqueJob)
            services.AddHostedService<AlertasEstoqueJob>();

        if (options.EnableProcessarRecebimentoJob)
            services.AddHostedService<ProcessarRecebimentoJob>();

        if (options.EnableRecalcularVelocidadesJob)
            services.AddHostedService<RecalcularVelocidadesJob>();

        if (options.EnableRelatorioMensalJob)
            services.AddHostedService<RelatorioMensalJob>();

        if (options.EnableDiagnosticoEmailReport)
            services.AddHostedService<DiagnosticoEmailReportJob>();

        if (options.EnableCobrancaAssinaturaJob)
            services.AddHostedService<CobrancaAssinaturaJob>();

        // FaturaBackfillJob (F5) — rodada unica para gerar Fatura para
        // CobrancaAssinatura historicas. Habilitar via env var apenas durante
        // migracao controlada; uma vez concluida a migracao, desabilitar.
        if (options.EnableFaturaBackfillJob)
            services.AddHostedService<FaturaBackfillJob>();

        // FaturaReconciliacaoJob (F6/F11) — consulta gateway hora em hora para
        // fechar gaps de webhooks perdidos. Pix funciona ponta-a-ponta desde F11
        // (IEfiPixService.ConsultarCobrancaAsync via GET /v2/cob/{txid}).
        if (options.EnableFaturaReconciliacaoJob)
            services.AddHostedService<FaturaReconciliacaoJob>();

        // FaturaVencimentoJob (F6) — diario, processa D-3, D-1 e marca Vencidas.
        if (options.EnableFaturaVencimentoJob)
            services.AddHostedService<FaturaVencimentoJob>();

        return services;
    }
}
