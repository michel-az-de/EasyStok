using System.Diagnostics;
using EasyStock.Application.UseCases.Analytics.Reposicao;

namespace EasyStock.Application.UseCases.Analytics.Dashboard;

public class GetDashboardUseCase(
    IAnalyticsRepository analyticsRepository,
    ObterReposicaoUseCase obterReposicao,
    ILogger<GetDashboardUseCase> logger)
{
    public async Task<GetDashboardResult> ExecuteAsync(GetDashboardCommand cmd)
    {
        UseCaseGuards.EnsureEmpresaId(cmd.EmpresaId);

        var stopwatch = Stopwatch.StartNew();
        var dashboard = await analyticsRepository.GetDashboardResumoAsync(cmd.EmpresaId, cmd.PeriodoDias, cmd.LojaId);

        // Fonte única (ADR-0039 / issue 748): a contagem "estoque crítico" do card (e do badge do
        // menu, que reusa AlertasEstoqueBaixo) passa a derivar da MESMA projeção por-produto,
        // contando {ESGOTADO, CRITICO}. Substitui a contagem antiga por-lote/por-Status. É mudança
        // de KPI de propósito (por-produto), e alinha card, badge e listas na mesma fonte.
        var reposicao = await obterReposicao.ExecuteAsync(new ObterReposicaoCommand(cmd.EmpresaId, cmd.LojaId));
        var precisaRepor = reposicao.Count(i => i.Estado is EstadoReposicao.Esgotado or EstadoReposicao.Critico);
        dashboard = dashboard with { AlertasEstoqueBaixo = precisaRepor };

        stopwatch.Stop();
        logger.LogInformation("Dashboard retrieved in {Ms}ms for empresa {EmpresaId}",
            stopwatch.ElapsedMilliseconds, cmd.EmpresaId);

        return GetDashboardResult.FromDto(dashboard);
    }
}
