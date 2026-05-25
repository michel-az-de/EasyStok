using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.UseCases.Analytics.Receita;

public class CalcularReceitaUseCase(
    IAnalyticsRepository analyticsRepository,
    ILogger<CalcularReceitaUseCase> logger)
{
    public async Task<IEnumerable<CalcularReceitaResult>> ExecuteAsync(CalcularReceitaCommand cmd)
    {
        UseCaseGuards.EnsureEmpresaId(cmd.EmpresaId);

        var items = await analyticsRepository.GetReceitaPorPeriodoAsync(cmd.EmpresaId, cmd.Meses, cmd.LojaId);
        var results = items.Select(CalcularReceitaResult.FromDto).ToList();

        logger.LogInformation("Calculated revenue for {Count} months for empresa {EmpresaId}",
            results.Count, cmd.EmpresaId);

        return results;
    }
}
