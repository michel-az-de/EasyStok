namespace EasyStock.Application.UseCases.Analytics.VendasPorCanal;

public class ObterVendasPorCanalUseCase(
    IAnalyticsRepository analyticsRepository,
    ILogger<ObterVendasPorCanalUseCase> logger)
{
    public async Task<IEnumerable<ObterVendasPorCanalResult>> ExecuteAsync(ObterVendasPorCanalCommand cmd)
    {
        UseCaseGuards.EnsureEmpresaId(cmd.EmpresaId);

        // #2487: data pura é o dia de Brasília; o "até" inclui o dia inteiro.
        var dataAte = DataUtc.FimDoPeriodoUtc(cmd.DataAte) ?? DateTime.UtcNow;
        var dataDe = DataUtc.InicioDoPeriodoUtc(cmd.DataDe) ?? dataAte.AddDays(-cmd.DiasPadrao);

        var items = await analyticsRepository.GetVendasPorCanalAsync(cmd.EmpresaId, dataDe, dataAte, cmd.LojaId);
        var results = items.Select(ObterVendasPorCanalResult.FromDto).ToList();

        logger.LogInformation("Retrieved sales by {Count} channels between {DataDe:yyyy-MM-dd} and {DataAte:yyyy-MM-dd} for empresa {EmpresaId}",
            results.Count, dataDe, dataAte, cmd.EmpresaId);

        return results;
    }
}
