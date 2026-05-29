using System.Diagnostics;

namespace EasyStock.Application.UseCases.Analytics.Dashboard;

public class GetDashboardUseCase(
    IAnalyticsRepository analyticsRepository,
    ILogger<GetDashboardUseCase> logger)
{
    public async Task<GetDashboardResult> ExecuteAsync(GetDashboardCommand cmd)
    {
        UseCaseGuards.EnsureEmpresaId(cmd.EmpresaId);

        var stopwatch = Stopwatch.StartNew();
        var dashboard = await analyticsRepository.GetDashboardResumoAsync(cmd.EmpresaId, cmd.PeriodoDias, cmd.LojaId);
        stopwatch.Stop();

        logger.LogInformation("Dashboard retrieved in {Ms}ms for empresa {EmpresaId}",
            stopwatch.ElapsedMilliseconds, cmd.EmpresaId);

        return GetDashboardResult.FromDto(dashboard);
    }
}
