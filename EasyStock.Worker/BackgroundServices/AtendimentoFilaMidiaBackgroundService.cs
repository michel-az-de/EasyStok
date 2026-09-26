using EasyStock.Application.Ports.Output;
using EasyStock.Application.UseCases.Atendimento.Webhook;

namespace EasyStock.Worker.BackgroundServices;

/// <summary>
/// Drena a fila em processo <see cref="FilaAtendimentoNomes.MidiaWhatsApp"/> (S03): o webhook
/// enfileira, este loop chama <see cref="ProcessarMidiaWhatsAppJobUseCase"/> fora da requisição.
/// <see cref="FilaAtendimentoNomes.TurnoAgente"/> ainda não tem consumidor — entra com S06.
/// <c>BackgroundQueueService</c> é em memória (não sobrevive a restart); perda de job nesta janela
/// é aceitável para mídia (o texto da mensagem já foi salvo, só o anexo atrasa).
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
}
