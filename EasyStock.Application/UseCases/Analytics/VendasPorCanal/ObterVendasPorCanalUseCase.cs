using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.UseCases.Analytics.VendasPorCanal;

public class ObterVendasPorCanalUseCase(
    IAnalyticsRepository analyticsRepository,
    ILogger<ObterVendasPorCanalUseCase> logger)
{
    public async Task<IEnumerable<ObterVendasPorCanalResult>> ExecuteAsync(ObterVendasPorCanalCommand cmd)
    {
        UseCaseGuards.EnsureEmpresaId(cmd.EmpresaId);

        var dataAte = cmd.DataAte ?? DateTime.UtcNow;
        var dataDe = cmd.DataDe ?? dataAte.AddDays(-cmd.DiasPadrao);

        var items = await analyticsRepository.GetVendasPorCanalAsync(cmd.EmpresaId, dataDe, dataAte, cmd.LojaId);
        var results = items.Select(ObterVendasPorCanalResult.FromDto).ToList();

        logger.LogInformation("Retrieved sales by {Count} channels between {DataDe:yyyy-MM-dd} and {DataAte:yyyy-MM-dd} for empresa {EmpresaId}",
            results.Count, dataDe, dataAte, cmd.EmpresaId);

        return results;
    }
}
