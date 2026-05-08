using EasyStock.Api.Configuration;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Fiscal.ReprocessarContingencia;
using Microsoft.Extensions.Options;

namespace EasyStock.Api.BackgroundServices;

/// <summary>
/// Roda a cada N minutos retransmitindo notas em contingência. Para cada
/// nota:
///  - Tenta gateway.RetransmitirContingenciaAsync.
///  - Sucesso → MarcarAutorizadaPosContingencia + outbox.
///  - Falha temporária → deixa pra próxima rodada.
///  - >24h sem sucesso → MarcarRejeitada + alerta crítico.
/// </summary>
public sealed class ReprocessarContingenciaJob(
    IServiceScopeFactory scopeFactory,
    IOptionsMonitor<BackgroundJobOptions> opts,
    ILogger<ReprocessarContingenciaJob> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        log.LogInformation("ReprocessarContingenciaJob iniciado.");

        while (!stoppingToken.IsCancellationRequested)
        {
            var nfce = opts.CurrentValue.Nfce;
            try
            {
                if (nfce.ReprocessarContingenciaEnabled)
                {
                    await ProcessarRodadaAsync(nfce.ReprocessarContingenciaBatchSize, stoppingToken);
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                log.LogError(ex, "Erro no loop ReprocessarContingenciaJob.");
            }

            try
            {
                await Task.Delay(nfce.ReprocessarContingenciaPeriod, stoppingToken);
            }
            catch (OperationCanceledException) { return; }
        }
    }

    private async Task ProcessarRodadaAsync(int batchSize, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<INotaFiscalRepository>();
        var useCase = scope.ServiceProvider.GetRequiredService<ReprocessarContingenciaUseCase>();

        var pendentes = await repo.ListarEmContingenciaAsync(batchSize, ct);
        if (pendentes.Count == 0) return;

        log.LogInformation("Processando {Count} nota(s) em contingência.", pendentes.Count);

        foreach (var nota in pendentes)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var result = await useCase.ExecuteAsync(new ReprocessarContingenciaCommand(nota.Id, nota.EmpresaId));
                log.LogInformation("Nota {Id} → {Status}", nota.Id, result.StatusFinal);
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Falha ao reprocessar contingência {Id}", nota.Id);
            }
        }
    }
}
