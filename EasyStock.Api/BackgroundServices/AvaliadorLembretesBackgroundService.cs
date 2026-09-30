using EasyStock.Application.UseCases.Atendimento.Lembretes;
using EasyStock.Infra.Postgre.Concurrency;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Api.BackgroundServices;

/// <summary>
/// Avaliador dos lembretes da dona (S43), a cada minuto: cria os automáticos, resolve os que
/// deixaram de valer e avisa os vencidos (<see cref="AvaliarLembretesUseCase"/>). Cross-tenant: liga o
/// bypass de RLS antes de o advisory lock abrir a conexão (mesmo motivo do <c>CaixaEsquecidoJob</c>), e
/// o lock deixa uma réplica só avaliando. Desligado por <c>BackgroundJobs:EnableAvaliadorLembretes=false</c>.
/// </summary>
public sealed class AvaliadorLembretesBackgroundService(
    IServiceProvider serviceProvider,
    IConfiguration configuration,
    ILogger<AvaliadorLembretesBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var intervalo = TimeSpan.FromSeconds(
            configuration.GetValue("Atendimento:Lembretes:PollingIntervalSeconds", defaultValue: 60));
        logger.LogInformation("AvaliadorLembretesBackgroundService iniciado — polling={Intervalo}.", intervalo);

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
                logger.LogError(ex, "AvaliadorLembretesBackgroundService: erro na rodada — continua no próximo tick.");
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

        await sp.GetRequiredService<PostgresAdvisoryLock>().TentarExecutarAsync(LockKeys.AvaliadorLembretes, async token =>
        {
            var resultado = await sp.GetRequiredService<AvaliarLembretesUseCase>().ExecuteAsync(token);
            if (resultado.Criados + resultado.Resolvidos + resultado.Avisados > 0)
                logger.LogInformation("Lembretes: criados={Criados} resolvidos={Resolvidos} avisados={Avisados}.",
                    resultado.Criados, resultado.Resolvidos, resultado.Avisados);
        }, ct);
    }
}
