using EasyStock.Application.UseCases.Atendimento;
using EasyStock.Application.UseCases.Atendimento.Webhook;

namespace EasyStock.Api.BackgroundServices;

/// <summary>
/// Consumidor da fila <see cref="FilaAtendimentoNomes.TurnoAgente"/> (S06): o webhook do WhatsApp
/// enfileira e este loop roda <see cref="ProcessarTurnoAgenteUseCase"/> fora da requisição, um escopo
/// de DI por job. Fica na Api porque a fila (<c>BackgroundQueueService</c>) é em memória e o webhook
/// que enfileira roda aqui. Perda de job num restart é aceitável: a mensagem já está salva e aparece
/// no console para a dona.
/// </summary>
public sealed class AtendimentoFilaTurnoAgenteBackgroundService(
    IServiceProvider serviceProvider,
    IQueueService queueService,
    IConfiguration configuration,
    ILogger<AtendimentoFilaTurnoAgenteBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var intervalo = TimeSpan.FromMilliseconds(
            configuration.GetValue("Atendimento:FilaTurnoAgente:PollingIntervalMs", defaultValue: 500));

        logger.LogInformation("AtendimentoFilaTurnoAgenteBackgroundService iniciado — polling={Intervalo}.", intervalo);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await queueService.ProcessQueueAsync<ProcessarTurnoAgenteJob>(
                    FilaAtendimentoNomes.TurnoAgente,
                    async job =>
                    {
                        using var scope = serviceProvider.CreateScope();
                        var useCase = scope.ServiceProvider.GetRequiredService<ProcessarTurnoAgenteUseCase>();
                        await useCase.ExecuteAsync(job, stoppingToken);
                    },
                    stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "AtendimentoFilaTurnoAgenteBackgroundService: erro na rodada, segue no próximo tick.");
            }

            try { await Task.Delay(intervalo, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }
}
