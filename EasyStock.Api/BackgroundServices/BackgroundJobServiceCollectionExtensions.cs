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

        services.AddHostedService<AnalisadorEstoqueBackgroundService>();

        var options = configuration.GetSection(BackgroundJobOptions.SectionName).Get<BackgroundJobOptions>()
            ?? new BackgroundJobOptions();

        if (options.EnableAlertasEstoqueJob)
            services.AddHostedService<AlertasEstoqueJob>();

        if (options.EnableProcessarRecebimentoJob)
            services.AddHostedService<ProcessarRecebimentoJob>();

        if (options.EnableRecalcularVelocidadesJob)
            services.AddHostedService<RecalcularVelocidadesJob>();

        if (options.EnableRelatorioMensalJob)
            services.AddHostedService<RelatorioMensalJob>();

        return services;
    }
}
