using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.UseCases.Analytics.Validade;

public class ObterValidadeUseCase(
    IAnalyticsRepository analyticsRepository,
    ILogger<ObterValidadeUseCase> logger)
{
    public async Task<(IEnumerable<ObterValidadeResult> Items, int TotalCount)> ExecuteAsync(ObterValidadeCommand cmd)
    {
        UseCaseGuards.EnsureEmpresaId(cmd.EmpresaId);

        var (items, totalCount) = await analyticsRepository.GetAlertasValidadeAsync(
            cmd.EmpresaId, cmd.Dias ?? 30, cmd.Page, cmd.PageSize, cmd.LojaId);

        var results = items.Select(ObterValidadeResult.FromDto).ToList();

        logger.LogInformation("Retrieved {Count} validity alerts for empresa {EmpresaId}",
            results.Count, cmd.EmpresaId);

        return (results, totalCount);
    }
}
