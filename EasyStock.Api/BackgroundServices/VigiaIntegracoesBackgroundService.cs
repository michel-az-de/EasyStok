using EasyStock.Application.UseCases.Integracoes;
using EasyStock.Infra.Postgre.Concurrency;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Api.BackgroundServices;

/// <summary>
/// Vigia das integrações (F16, #1246), a cada 15 min (<see cref="VigiarIntegracoesUseCase"/>): testa a
/// chave em uso de cada integração e lembra a dona da que caiu. A primeira rodada sai logo na subida,
/// para o estado das chaves globais (em memória) voltar depois de um restart. Cross-tenant: liga o
/// bypass de RLS antes de o advisory lock abrir a conexão (mesmo molde do avaliador de lembretes), e o
/// lock deixa uma réplica só vigiando. Desligado por <c>BackgroundJobs:EnableVigiaIntegracoes=false</c>.
/// </summary>
public sealed class VigiaIntegracoesBackgroundService(
    IServiceProvider serviceProvider,
    IConfiguration configuration,
    ILogger<VigiaIntegracoesBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var intervalo = TimeSpan.FromSeconds(configuration.GetValue(
            "Integracoes:Vigia:IntervaloSegundos", defaultValue: (int)VigiarIntegracoesUseCase.Intervalo.TotalSeconds));
        logger.LogInformation("VigiaIntegracoesBackgroundService iniciado — intervalo={Intervalo}.", intervalo);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RodadaAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "VigiaIntegracoesBackgroundService: erro na rodada — continua no próximo tick.");
            }

            try { await Task.Delay(intervalo, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task RodadaAsync(CancellationToken ct)
    {
        using var scope = serviceProvider.CreateScope();
        var sp = scope.ServiceProvider;
        using var _ = sp.GetRequiredService<EasyStockDbContext>().UseRowLevelSecurityBypass();

        await sp.GetRequiredService<PostgresAdvisoryLock>().TentarExecutarAsync(LockKeys.VigiaIntegracoes, async token =>
        {
            var r = await sp.GetRequiredService<VigiarIntegracoesUseCase>().ExecuteAsync(token);
            if (r.Falhas + r.LembretesCriados + r.LembretesResolvidos > 0)
                logger.LogInformation("Vigia das integrações: testadas={Testadas} falhas={Falhas} lembretes criados={Criados} resolvidos={Resolvidos}.",
                    r.Testadas, r.Falhas, r.LembretesCriados, r.LembretesResolvidos);
        }, ct);
    }
}
