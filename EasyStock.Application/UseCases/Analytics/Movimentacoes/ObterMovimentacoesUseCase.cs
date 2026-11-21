namespace EasyStock.Application.UseCases.Analytics.Movimentacoes;

public class ObterMovimentacoesUseCase(
    IAnalyticsRepository analyticsRepository,
    ILogger<ObterMovimentacoesUseCase> logger)
{
    public async Task<IEnumerable<ObterMovimentacoesResult>> ExecuteAsync(ObterMovimentacoesCommand cmd)
    {
        UseCaseGuards.EnsureEmpresaId(cmd.EmpresaId);

        // #2487: data pura é o dia de Brasília; o "até" inclui o dia inteiro.
        var dataAte = DataUtc.FimDoPeriodoUtc(cmd.DataAte) ?? DateTime.UtcNow;
        var dataDe = DataUtc.InicioDoPeriodoUtc(cmd.DataDe) ?? dataAte.AddDays(-cmd.DiasPadrao);

        var items = await analyticsRepository.GetMovimentacoesResumoAsync(cmd.EmpresaId, dataDe, dataAte, cmd.Tipo, cmd.LojaId);
        var results = items.Select(ObterMovimentacoesResult.FromDto).ToList();

        logger.LogInformation("Retrieved {Count} movements between {DataDe:yyyy-MM-dd} and {DataAte:yyyy-MM-dd} for empresa {EmpresaId}",
            results.Count, dataDe, dataAte, cmd.EmpresaId);

        return results;
    }
}
