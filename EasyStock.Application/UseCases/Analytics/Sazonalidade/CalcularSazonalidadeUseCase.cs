using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.UseCases.Analytics.Sazonalidade;

public class CalcularSazonalidadeUseCase(
    IAnalyticsRepository analyticsRepository,
    ILogger<CalcularSazonalidadeUseCase> logger)
{
    public async Task<IEnumerable<CalcularSazonalidadeResult>> ExecuteAsync(CalcularSazonalidadeCommand cmd)
    {
        UseCaseGuards.EnsureEmpresaId(cmd.EmpresaId);
        if (cmd.ProdutoId == Guid.Empty)
            throw new UseCaseValidationException("ProdutoId é obrigatório");

        var items = await analyticsRepository.GetSazonalidadeAsync(cmd.EmpresaId, cmd.ProdutoId, cmd.Meses, cmd.LojaId);
        var results = items.Select(CalcularSazonalidadeResult.FromDto).ToList();

        logger.LogInformation("Calculated seasonality for {Count} months for produto {ProdutoId}",
            results.Count, cmd.ProdutoId);

        return results;
    }
}
