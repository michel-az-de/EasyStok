using EasyStock.Application.Ports.Output.Reporting;
using EasyStock.Application.Ports.Output.Storage;
using EasyStock.Application.Reporting;

namespace EasyStock.Application.UseCases.Reports;

/// <summary>
/// Watchdog do motor de relatórios (ADR-R03, §6.3): recicla as runs cujo Worker parou de confirmar a execução e apaga o
/// envio interrompido das que fecham sem sucesso (#2788). A run só grava a chave quando conclui, então a limpeza de
/// artefatos não enxerga esse arquivo. Run devolvida à fila fica como está: a nova tentativa grava na mesma chave.
/// </summary>
public sealed class ReciclarExecucoesVencidasUseCase(
    IReportRunRepository repo,
    IFileStorage storage,
    ILogger<ReciclarExecucoesVencidasUseCase> logger)
{
    /// <summary>Devolve quantas runs foram recicladas.</summary>
    public async Task<int> ExecuteAsync(CancellationToken ct)
    {
        var reciclagem = await repo.ReclaimExpiredLeasesAsync(ct);

        foreach (var run in reciclagem.FechadasSemSucesso)
        {
            var chave = ReportArtefato.Chave(run);
            try
            {
                await storage.DeleteAsync(chave, CancellationToken.None);
            }
            catch (Exception ex)
            {
                // Melhor esforço, como a limpeza de artefatos: a run já está fechada no banco.
                logger.LogWarning(ex,
                    "ReportWatchdog: falha ao apagar o envio interrompido '{Key}' da run {RunId} (melhor esforço).",
                    chave, run.Id);
            }
        }

        return reciclagem.Total;
    }
}
