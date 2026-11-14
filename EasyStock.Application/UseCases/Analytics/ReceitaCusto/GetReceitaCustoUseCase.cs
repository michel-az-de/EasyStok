namespace EasyStock.Application.UseCases.Analytics.ReceitaCusto;

public class GetReceitaCustoUseCase(IAnalyticsRepository analyticsRepository)
{
    public async Task<IReadOnlyList<ReceitaCustoDia>> ExecuteAsync(GetReceitaCustoCommand cmd)
    {
        UseCaseGuards.EnsureEmpresaId(cmd.EmpresaId);

        var ate = DateTime.UtcNow; // janela em instantes UTC; o fuso (tz) só agrupa no repositório (#2561)
        var de = ate.AddDays(-cmd.PeriodoDias);

        return await analyticsRepository.GetReceitaCustoSerieAsync(cmd.EmpresaId, de, ate, cmd.LojaId, cmd.TimezoneOffsetMinutes);
    }
}
