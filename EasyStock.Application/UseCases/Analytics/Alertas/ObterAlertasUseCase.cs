using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.UseCases.Analytics.Alertas;

public class ObterAlertasUseCase(
    IAnalyticsRepository analyticsRepository,
    ILogger<ObterAlertasUseCase> logger)
{
    public async Task<(IEnumerable<ObterAlertasResult> Items, int TotalCount)> ExecuteAsync(ObterAlertasCommand cmd)
    {
        UseCaseGuards.EnsureEmpresaId(cmd.EmpresaId);

        var (items, totalCount) = await analyticsRepository.GetAlertasValidadeAsync(
            cmd.EmpresaId, cmd.Dias ?? 30, cmd.Page, cmd.PageSize, cmd.LojaId);

        var results = items.Select(ObterAlertasResult.FromDto).ToList();

        logger.LogInformation("Retrieved {Count} expiry alerts for empresa {EmpresaId}",
            results.Count, cmd.EmpresaId);

        return (results, totalCount);
    }
}
