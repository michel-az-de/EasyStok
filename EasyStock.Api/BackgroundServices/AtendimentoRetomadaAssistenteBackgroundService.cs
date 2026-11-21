using EasyStock.Application.Services.Atendimento;
using EasyStock.Application.UseCases.Atendimento;
using EasyStock.Infra.Postgre.Data;
using Microsoft.Extensions.Options;

namespace EasyStock.Api.BackgroundServices;

/// <summary>
/// #3068: varredura da conversa parada com a equipe. A cada passada enfileira o turno marcado como retomada para as
/// candidatas de todas as lojas (bypass de RLS, como a recuperação de turnos); o turno roda depois no tenant dele e
/// decide. Fica na Api pelo mesmo motivo do consumidor: a fila do turno é em memória e vive aqui.
/// <c>Atendimento:RetomadaAssistente:Segundos=0</c> desliga sem deploy de código.
/// </summary>
public sealed class AtendimentoRetomadaAssistenteBackgroundService(
    IServiceProvider serviceProvider,
    IOptions<RetomadaAssistenteOptions> opcoes,
    ILogger<AtendimentoRetomadaAssistenteBackgroundService> logger) : BackgroundService
{
    public const int VarreduraMinimaSegundos = 5;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var prazo = opcoes.Value.Prazo;
        if (prazo <= TimeSpan.Zero)
        {
            logger.LogWarning("AtendimentoRetomadaAssistenteBackgroundService desligado (Atendimento:RetomadaAssistente:Segundos=0).");
            return;
        }

        var intervalo = TimeSpan.FromSeconds(Math.Max(VarreduraMinimaSegundos, opcoes.Value.VarreduraSegundos));
        // Warning: produção só grava de Warning para cima, e a linha é uma por start.
        logger.LogWarning("AtendimentoRetomadaAssistenteBackgroundService iniciado: prazo={Prazo}, varredura={Intervalo}.", prazo, intervalo);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = serviceProvider.CreateScope();
                using var _ = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>().UseRowLevelSecurityBypass();
                await scope.ServiceProvider.GetRequiredService<EnfileirarRetomadasDoAssistenteUseCase>()
                    .ExecuteAsync(DateTime.UtcNow, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "AtendimentoRetomadaAssistenteBackgroundService: erro na varredura, segue na próxima.");
            }

            try { await Task.Delay(intervalo, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }
}
