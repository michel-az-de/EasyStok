using EasyStock.Application.UseCases.Operacao.Impressao;

namespace EasyStock.Api.BackgroundServices;

/// <summary>
/// Alerta de canhoto parado na fila (S20). A cada 2 min, pendente há mais de 3 min vira
/// <c>impressao.atrasada</c> no SSE da empresa (<see cref="AlertarImpressoesAtrasadasUseCase"/>), e o console
/// avisa a dona. Só lê e publica evento de UI: sem advisory lock, cada instância avisa os próprios clientes
/// SSE. Desligado por <c>BackgroundJobs:EnableImpressaoPendenteAlerta=false</c>.
/// </summary>
public sealed class ImpressaoPendenteAlertaJob(
    IServiceProvider serviceProvider,
    ILogger<ImpressaoPendenteAlertaJob> logger) : BackgroundService
{
    private static readonly TimeSpan Intervalo = TimeSpan.FromMinutes(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("ImpressaoPendenteAlertaJob iniciado — intervalo={Intervalo}.", Intervalo);
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await Task.Delay(Intervalo, stoppingToken); }
            catch (OperationCanceledException) { break; }

            try
            {
                using var scope = serviceProvider.CreateScope();
                var atrasadas = await scope.ServiceProvider.GetRequiredService<AlertarImpressoesAtrasadasUseCase>()
                    .ExecuteAsync(stoppingToken);
                if (atrasadas > 0)
                    logger.LogWarning("ImpressaoPendenteAlertaJob: {Atrasadas} canhoto(s) pendente(s) há mais de 3 min.", atrasadas);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                logger.LogError(ex, "ImpressaoPendenteAlertaJob: erro na rodada — continua no próximo tick.");
            }
        }
    }
}
