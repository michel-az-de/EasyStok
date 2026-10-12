using EasyStock.Application.Ports.Output.Persistence.Operacao;
using EasyStock.Application.UseCases.Operacao.Impressao;

namespace EasyStock.Api.BackgroundServices;

/// <summary>
/// Alerta de canhoto parado na fila (S20). A cada 2 min, pendente há mais de 3 min vira
/// <c>impressao.atrasada</c> no SSE da empresa (<see cref="AlertarImpressoesAtrasadasUseCase"/>), e o console
/// avisa a dona. O SSE é por instância (sem advisory lock) e repete a cada rodada. Por impressão atrasada o job
/// abre um escopo próprio, com o tenant dela, e chama <see cref="NotificarImpressaoTravadaUseCase"/>, que
/// enfileira o <c>PrazoEstourado</c> (e-mail e WhatsApp) uma vez só, com pré-checagem do
/// <c>CorrelationId</c> determinístico (N11). Desligado por <c>BackgroundJobs:EnableImpressaoPendenteAlerta=false</c>.
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
                IReadOnlyList<ImpressaoAtrasada> atrasadas;
                using (var scope = serviceProvider.CreateScope())
                {
                    atrasadas = await scope.ServiceProvider.GetRequiredService<AlertarImpressoesAtrasadasUseCase>()
                        .ExecuteAsync(stoppingToken);
                }

                if (atrasadas.Count > 0)
                {
                    logger.LogWarning("ImpressaoPendenteAlertaJob: {Atrasadas} canhoto(s) pendente(s) além do limite.", atrasadas.Count);
                    await AvisarExternoAsync(atrasadas, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                logger.LogError(ex, "ImpressaoPendenteAlertaJob: erro na rodada — continua no próximo tick.");
            }
        }
    }

    private async Task AvisarExternoAsync(IReadOnlyList<ImpressaoAtrasada> atrasadas, CancellationToken ct)
    {
        foreach (var impressao in atrasadas)
        {
            try
            {
                // Um escopo por impressão: DbContext limpo e o tenant da impressão só nele (RLS).
                using var scope = serviceProvider.CreateScope();
                await scope.ServiceProvider.GetRequiredService<NotificarImpressaoTravadaUseCase>()
                    .ExecuteAsync(impressao, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "ImpressaoPendenteAlertaJob: falha ao enfileirar o aviso da impressão {ImpressaoId}.",
                    impressao.ImpressaoId);
            }
        }
    }
}
