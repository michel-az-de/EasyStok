using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;
using EasyStock.Application.UseCases.GerenciarProduto;

namespace EasyStock.Api.BackgroundServices;

/// <summary>
/// Pré-aquece o cache Redis logo após a inicialização da aplicação.
/// Evita as regressões de performance observadas nos primeiros requests após deploy/restart,
/// onde 7 endpoints críticos degradaram até +319%.
/// </summary>
public sealed class CacheWarmupService(
    IServiceScopeFactory scopeFactory,
    ILogger<CacheWarmupService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Aguarda 5 segundos para o app estar completamente pronto (migrations, seed)
        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);

        logger.LogInformation("Cache warm-up iniciado.");
        var sw = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            using var scope = scopeFactory.CreateScope();
            var cache = scope.ServiceProvider.GetService<ICacheService>();
            if (cache is null)
            {
                logger.LogDebug("Cache warm-up ignorado — ICacheService não registrado.");
                return;
            }

            var empresaRepo   = scope.ServiceProvider.GetRequiredService<IEmpresaRepository>();
            var analyticsRepo = scope.ServiceProvider.GetService<IAnalyticsRepository>();
            var lojaRepo      = scope.ServiceProvider.GetRequiredService<ILojaRepository>();

            // Buscar todas as empresas ativas para aquecer o cache de cada uma
            var empresas = await empresaRepo.GetAllAsync();

            int warmed = 0;
            foreach (var empresa in empresas)
            {
                if (stoppingToken.IsCancellationRequested) break;

                try
                {
                    // Dashboard analytics (mais pesado — 30 dias)
                    if (analyticsRepo is not null)
                    {
                        await analyticsRepo.GetDashboardResumoAsync(empresa.Id, 30);
                        warmed++;
                    }

                    // Lista de lojas
                    await lojaRepo.GetByEmpresaAsync(empresa.Id);
                    warmed++;
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Cache warm-up falhou para empresa {EmpresaId}.", empresa.Id);
                }
            }

            sw.Stop();
            logger.LogInformation(
                "Cache warm-up concluído: {Warmed} entradas em {ElapsedMs}ms.",
                warmed, sw.ElapsedMilliseconds);
        }
        catch (OperationCanceledException) { /* shutdown normal */ }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Cache warm-up falhou — aplicação continuará sem cache pré-aquecido.");
        }
    }
}
