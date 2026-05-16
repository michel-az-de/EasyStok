using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;

namespace EasyStock.Application.UseCases.Analytics.ReceitaCusto;

public class GetReceitaCustoUseCase(IAnalyticsRepository analyticsRepository)
{
    public async Task<IReadOnlyList<ReceitaCustoDia>> ExecuteAsync(GetReceitaCustoCommand cmd)
    {
        UseCaseGuards.EnsureEmpresaId(cmd.EmpresaId);

        var ate = DateTime.UtcNow.AddMinutes(-cmd.TimezoneOffsetMinutes);
        var de = ate.AddDays(-cmd.PeriodoDias);

        return await analyticsRepository.GetReceitaCustoSerieAsync(cmd.EmpresaId, de, ate, cmd.LojaId, cmd.TimezoneOffsetMinutes);
    }
}
