using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.UseCases.Analytics.Movimentacoes;

public class ObterMovimentacoesUseCase(
    IAnalyticsRepository analyticsRepository,
    ILogger<ObterMovimentacoesUseCase> logger)
{
    public async Task<IEnumerable<ObterMovimentacoesResult>> ExecuteAsync(ObterMovimentacoesCommand cmd)
    {
        UseCaseGuards.EnsureEmpresaId(cmd.EmpresaId);

        var dataAte = cmd.DataAte ?? DateTime.UtcNow;
        var dataDe = cmd.DataDe ?? dataAte.AddDays(-cmd.DiasPadrao);

        var items = await analyticsRepository.GetMovimentacoesResumoAsync(cmd.EmpresaId, dataDe, dataAte, cmd.Tipo, cmd.LojaId);
        var results = items.Select(ObterMovimentacoesResult.FromDto).ToList();

        logger.LogInformation("Retrieved {Count} movements between {DataDe:yyyy-MM-dd} and {DataAte:yyyy-MM-dd} for empresa {EmpresaId}",
            results.Count, dataDe, dataAte, cmd.EmpresaId);

        return results;
    }
}
