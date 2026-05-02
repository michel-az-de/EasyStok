using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.UseCases.Analytics.Reposicao;

public class CalcularReposicaoUseCase(
    IAnalyticsRepository analyticsRepository,
    ILogger<CalcularReposicaoUseCase> logger)
{
    public async Task<(IEnumerable<CalcularReposicaoResult> Items, int TotalCount)> ExecuteAsync(CalcularReposicaoCommand cmd)
    {
        UseCaseGuards.EnsureEmpresaId(cmd.EmpresaId);

        var (items, totalCount) = await analyticsRepository.GetSugestaoReposicaoDetalhadaAsync(
            cmd.EmpresaId, cmd.DiasHistorico, cmd.Page, cmd.PageSize, cmd.LojaId);

        var results = items.Select(CalcularReposicaoResult.FromDto).ToList();

        logger.LogInformation("Calculated {Count} replenishment suggestions for empresa {EmpresaId}",
            results.Count, cmd.EmpresaId);

        return (results, totalCount);
    }
}
