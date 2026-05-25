using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.UseCases.Analytics.Parados;

public class ObterParadosUseCase(
    IAnalyticsRepository analyticsRepository,
    ILogger<ObterParadosUseCase> logger)
{
    public async Task<(IEnumerable<ObterParadosResult> Items, int TotalCount)> ExecuteAsync(ObterParadosCommand cmd)
    {
        UseCaseGuards.EnsureEmpresaId(cmd.EmpresaId);

        var (items, totalCount) = await analyticsRepository.GetItensParadosDetalhadosAsync(
            cmd.EmpresaId, cmd.DiasSemMovimento ?? 90, cmd.Page, cmd.PageSize, cmd.LojaId);

        var results = items.Select(ObterParadosResult.FromDto).ToList();

        logger.LogInformation("Retrieved {Count} idle items for empresa {EmpresaId}",
            results.Count, cmd.EmpresaId);

        return (results, totalCount);
    }
}
