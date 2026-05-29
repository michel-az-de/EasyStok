namespace EasyStock.Application.UseCases.Analytics.Projecoes;

public class CalcularProjecoesUseCase(
    IAnalyticsRepository analyticsRepository,
    ILogger<CalcularProjecoesUseCase> logger)
{
    public async Task<(IEnumerable<CalcularProjecoesResult> Items, int TotalCount)> ExecuteAsync(CalcularProjecoesCommand cmd)
    {
        UseCaseGuards.EnsureEmpresaId(cmd.EmpresaId);

        var (items, totalCount) = await analyticsRepository.GetProjecaoRupturaAsync(
            cmd.EmpresaId, cmd.DiasHistorico, cmd.Page, cmd.PageSize, cmd.LojaId);

        var results = items.Select(CalcularProjecoesResult.FromDto).ToList();

        logger.LogInformation("Calculated {Count} projections for empresa {EmpresaId}",
            results.Count, cmd.EmpresaId);

        return (results, totalCount);
    }
}
