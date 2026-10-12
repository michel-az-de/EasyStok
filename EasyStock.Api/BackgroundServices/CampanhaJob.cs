using EasyStock.Application.Ports.Output.Persistence.Campanhas;
using EasyStock.Application.UseCases.Campanhas;
using EasyStock.Infra.Postgre.Concurrency;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Api.BackgroundServices;

/// <summary>
/// Job das campanhas (S30), a cada minuto: dispara a primeira onda das agendadas vencidas, concilia
/// os enfileirados com o outbox (enviado ou falhou), conclui a onda e, no encerramento, encerra e
/// manda o lembrete (<see cref="ProcessarCampanhaUseCase"/>). Onda seguinte só pela dona. A lista sai
/// de um escopo com bypass de RLS e advisory lock (uma réplica por vez, sem disparo em dobro); cada
/// campanha roda num escopo próprio com o tenant dela, e a falha de uma não para as outras.
/// Desligado por <c>BackgroundJobs:EnableCampanhaJob=false</c>: as campanhas ficam <c>Agendada</c>.
/// </summary>
public sealed class CampanhaJob(
    IServiceProvider serviceProvider,
    IConfiguration configuration,
    TimeProvider relogio,
    ILogger<CampanhaJob> logger) : BackgroundService
{
    private const int LotePorRodada = 100;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var intervalo = TimeSpan.FromSeconds(
            configuration.GetValue("Campanhas:PollingIntervalSeconds", defaultValue: 60));
        logger.LogInformation("CampanhaJob iniciado — polling={Intervalo}.", intervalo);

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
                logger.LogError(ex, "CampanhaJob: erro na rodada — continua no próximo tick.");
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

        await sp.GetRequiredService<PostgresAdvisoryLock>().TentarExecutarAsync(LockKeys.CampanhaJob, async token =>
        {
            var campanhas = await sp.GetRequiredService<ICampanhaRepository>()
                .ListarParaProcessarAsync(relogio.GetUtcNow().UtcDateTime, LotePorRodada, token);
            foreach (var campanha in campanhas)
                await ProcessarAsync(campanha, token);
        }, ct);
    }

    private async Task ProcessarAsync(CampanhaParaProcessar campanha, CancellationToken ct)
    {
        try
        {
            using var scope = serviceProvider.CreateScope();
            scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>().SetCurrentTenant(campanha.EmpresaId);
            var r = await scope.ServiceProvider.GetRequiredService<ProcessarCampanhaUseCase>()
                .ExecuteAsync(campanha.EmpresaId, campanha.CampanhaId, ct);
            if (r.OndaDisparada is not null || r.Encerrada || r.Enviados + r.Falhas > 0)
                logger.LogInformation(
                    "Campanha {CampanhaId}: onda={Onda} enviados={Enviados} falhas={Falhas} ondaConcluida={Concluida} encerrada={Encerrada} lembretes={Lembretes}.",
                    campanha.CampanhaId, r.OndaDisparada, r.Enviados, r.Falhas, r.OndaConcluida, r.Encerrada, r.Lembretes);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Kill switch, template ausente ou falha do banco: a campanha fica como estava e volta no próximo tick.
            logger.LogError(ex, "CampanhaJob: campanha {CampanhaId} da empresa {EmpresaId} não processada.",
                campanha.CampanhaId, campanha.EmpresaId);
        }
    }
}
