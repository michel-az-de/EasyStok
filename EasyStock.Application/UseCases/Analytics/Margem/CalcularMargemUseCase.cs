namespace EasyStock.Application.UseCases.Analytics.Margem;

public class CalcularMargemUseCase(
    IAnalyticsRepository analyticsRepository,
    ILogger<CalcularMargemUseCase> logger)
{
    public async Task<(IEnumerable<CalcularMargemResult> Items, int TotalCount)> ExecuteAsync(CalcularMargemCommand cmd)
    {
        UseCaseGuards.EnsureEmpresaId(cmd.EmpresaId);

        var items = await analyticsRepository.GetMargemPorProdutoAsync(cmd.EmpresaId, cmd.Dias, cmd.Page, cmd.PageSize, cmd.LojaId);
        var results = items.Select(CalcularMargemResult.FromDto).ToList();

        logger.LogInformation("Calculated margin for {Count} products for empresa {EmpresaId}",
            results.Count, cmd.EmpresaId);

        return (results, results.Count);
    }
}
