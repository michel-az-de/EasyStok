using EasyStock.Application.UseCases.Atendimento.Avisos;

namespace EasyStock.Api.BackgroundServices;

/// <summary>
/// Vigia da operação (#2096). A cada 60 s percorre as empresas, cada uma no próprio escopo com o tenant ligado
/// (<see cref="RodadaPorEmpresa"/>), e roda o <see cref="VigiarOperacaoUseCase"/>. A trava de uma vez só é a chave de
/// correlação do evento; duas instâncias no mesmo minuto podem bater no índice único e a rodada seguinte segue limpa.
/// Desligado por <c>BackgroundJobs:EnableVigiaOperacao=false</c>.
/// </summary>
public sealed class VigiaOperacaoJob(
    IServiceScopeFactory scopeFactory,
    TimeProvider relogio,
    ILogger<VigiaOperacaoJob> logger) : BackgroundService
{
    private static readonly TimeSpan Intervalo = TimeSpan.FromSeconds(60);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("VigiaOperacaoJob iniciado");
        try { await Task.Delay(TimeSpan.FromSeconds(45), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var agora = relogio.GetUtcNow().UtcDateTime;
                var rodada = await RodadaPorEmpresa.ExecutarAsync(scopeFactory, logger, nameof(VigiaOperacaoJob),
                    (sp, empresaId, ct) => sp.GetRequiredService<VigiarOperacaoUseCase>().ExecuteAsync(empresaId, agora, ct),
                    stoppingToken);
                if (rodada.Falhas > 0)
                    logger.LogWarning("VigiaOperacaoJob: {Falhas} de {Empresas} empresas falharam na rodada", rodada.Falhas, rodada.Empresas);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                logger.LogError(ex, "VigiaOperacaoJob: erro na rodada.");
            }

            try { await Task.Delay(Intervalo, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }
}
