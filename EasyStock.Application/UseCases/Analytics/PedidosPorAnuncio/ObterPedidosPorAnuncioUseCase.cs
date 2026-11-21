namespace EasyStock.Application.UseCases.Analytics.PedidosPorAnuncio;

/// <summary>#2928 (S5): pedidos, valor e ticket médio por plataforma e campanha do anúncio.</summary>
public class ObterPedidosPorAnuncioUseCase(
    IAnalyticsRepository analyticsRepository,
    ILogger<ObterPedidosPorAnuncioUseCase> logger)
{
    public async Task<IEnumerable<ObterPedidosPorAnuncioResult>> ExecuteAsync(ObterPedidosPorAnuncioCommand cmd)
    {
        UseCaseGuards.EnsureEmpresaId(cmd.EmpresaId);

        // Data pura é o dia de Brasília; o "até" inclui o dia inteiro (#2487).
        var dataAte = DataUtc.FimDoPeriodoUtc(cmd.DataAte) ?? DateTime.UtcNow;
        var dataDe = DataUtc.InicioDoPeriodoUtc(cmd.DataDe) ?? dataAte.AddDays(-cmd.DiasPadrao);

        var items = await analyticsRepository.GetPedidosPorAnuncioAsync(cmd.EmpresaId, dataDe, dataAte);
        var results = items
            .Select(ObterPedidosPorAnuncioResult.FromDto)
            .OrderBy(r => r.Plataforma == ObterPedidosPorAnuncioResult.PlataformaSemOrigem)
            .ThenByDescending(r => r.ValorTotal)
            .ToList();

        logger.LogInformation("Retrieved {Count} ad-origin rows between {DataDe:yyyy-MM-dd} and {DataAte:yyyy-MM-dd} for empresa {EmpresaId}",
            results.Count, dataDe, dataAte, cmd.EmpresaId);

        return results;
    }
}
