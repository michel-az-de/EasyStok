using EasyStock.Application.UseCases.Atendimento.Webhook;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Api.BackgroundServices;

/// <summary>
/// Drena a fila em processo <see cref="FilaAtendimentoNomes.MidiaWhatsApp"/> (S03): o webhook
/// enfileira, este loop chama <see cref="ProcessarMidiaWhatsAppJobUseCase"/> fora da requisição.
/// <see cref="FilaAtendimentoNomes.TurnoAgente"/> tem consumidor próprio (<c>AtendimentoFilaTurnoAgenteBackgroundService</c>, S06).
/// Roda no processo da API, e não no Worker, porque <c>BackgroundQueueService</c> é em memória: só o
/// processo que enfileira enxerga a fila. A fila não sobrevive a restart: por isso a pendência também fica na
/// mensagem e a varredura (na partida e a cada <c>Atendimento:FilaMidia:VarreduraIntervalSeconds</c>) retoma os
/// anexos vencidos e as novas tentativas depois de falha (#1397), no padrão bypass+tenant do reenvio.
/// </summary>
public sealed class AtendimentoFilaMidiaBackgroundService(
    IServiceProvider serviceProvider,
    IQueueService queueService,
    Microsoft.Extensions.Configuration.IConfiguration configuration,
    ILogger<AtendimentoFilaMidiaBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var pollingInterval = TimeSpan.FromSeconds(
            configuration.GetValue("Atendimento:FilaMidia:PollingIntervalSeconds", defaultValue: 5));

        var intervaloVarredura = TimeSpan.FromSeconds(
            configuration.GetValue("Atendimento:FilaMidia:VarreduraIntervalSeconds", defaultValue: 30));
        var proximaVarredura = DateTime.MinValue; // a primeira rodada varre: pendência de antes do restart

        logger.LogInformation("AtendimentoFilaMidiaBackgroundService iniciado — polling={Interval}.", pollingInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await queueService.ProcessQueueAsync<ArmazenarMidiaWhatsAppJob>(
                    FilaAtendimentoNomes.MidiaWhatsApp,
                    async job =>
                    {
                        using var scope = serviceProvider.CreateScope();
                        var processador = scope.ServiceProvider.GetRequiredService<ProcessarMidiaWhatsAppJobUseCase>();
                        await processador.ExecuteAsync(job, stoppingToken);
                    },
                    stoppingToken);

                if (DateTime.UtcNow >= proximaVarredura)
                {
                    proximaVarredura = DateTime.UtcNow + intervaloVarredura;
                    await VarrerPendentesAsync(stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "AtendimentoFilaMidiaBackgroundService: erro na rodada — continuando próximo tick.");
            }

            try { await Task.Delay(pollingInterval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }

        logger.LogInformation("AtendimentoFilaMidiaBackgroundService finalizado.");
    }

    private const int LotePorVarredura = 20;

    private async Task VarrerPendentesAsync(CancellationToken ct)
    {
        IReadOnlyList<ArmazenarMidiaWhatsAppJob> jobs;
        using (var scope = serviceProvider.CreateScope())
        {
            using var _ = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>().UseRowLevelSecurityBypass();
            jobs = await scope.ServiceProvider.GetRequiredService<ReservarMidiasPendentesUseCase>()
                .ExecuteAsync(LotePorVarredura, ct);
        }

        foreach (var job in jobs)
        {
            try
            {
                using var scope = serviceProvider.CreateScope();
                // O use case define o tenant do job antes de consultar (RLS, ADR-0010).
                await scope.ServiceProvider.GetRequiredService<ProcessarMidiaWhatsAppJobUseCase>().ExecuteAsync(job, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Varredura de mídia: anexo do wamid {Wamid} não rodou.", job.Wamid);
            }
        }
    }
}
