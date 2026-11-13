using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Analytics.Movimentacoes;
using EasyStock.Application.UseCases.Analytics.VendasPorCanal;
using EasyStock.Application.UseCases.ListarLotes;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyStock.Application.Tests.UseCases;

/// <summary>
/// #2487: <c>de</c>/<c>desde</c> e <c>ate</c> chegam da query como data pura (Kind Unspecified). Sem normalizar,
/// <c>ate=2026-11-13</c> virava 13/11 00:00 UTC e escondia o último dia inteiro. Data pura é o dia de Brasília.
/// </summary>
public class FiltroPeriodoDataPuraTests
{
    private static readonly Guid EmpresaId = Guid.NewGuid();
    private static readonly DateTime De = new(2026, 11, 1);
    private static readonly DateTime Ate = new(2026, 11, 13);
    private static readonly DateTime DeUtc = new(2026, 11, 1, 3, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime AteUtc = new DateTime(2026, 11, 14, 3, 0, 0, DateTimeKind.Utc).AddTicks(-10);

    [Fact]
    public async Task ListarLotes_DataPura_IncluiODiaInteiroDeBrasilia()
    {
        var repo = Substitute.For<ILoteRepository>();
        repo.ListAsync(default, default, default, default, default, default, default, default, default, default)
            .ReturnsForAnyArgs((Enumerable.Empty<Lote>(), 0));

        await new ListarLotesUseCase(repo).ExecuteAsync(new ListarLotesQuery(EmpresaId, Desde: De, Ate: Ate));

        await repo.Received(1).ListAsync(EmpresaId, 1, 30, null, DeUtc, AteUtc,
            null, "dataproducao", "desc", null);
    }

    [Fact]
    public async Task AnalyticsMovimentacoes_DataPura_IncluiODiaInteiroDeBrasilia()
    {
        var repo = Substitute.For<IAnalyticsRepository>();
        var useCase = new ObterMovimentacoesUseCase(repo, NullLogger<ObterMovimentacoesUseCase>.Instance);

        await useCase.ExecuteAsync(new ObterMovimentacoesCommand(EmpresaId, De, Ate));

        await repo.Received(1).GetMovimentacoesResumoAsync(EmpresaId, DeUtc, AteUtc,
            Arg.Any<TipoMovimentacaoEstoque?>(), Arg.Any<Guid?>());
    }

    [Fact]
    public async Task VendasPorCanal_DataPura_IncluiODiaInteiroDeBrasilia()
    {
        var repo = Substitute.For<IAnalyticsRepository>();
        var useCase = new ObterVendasPorCanalUseCase(repo, NullLogger<ObterVendasPorCanalUseCase>.Instance);

        await useCase.ExecuteAsync(new ObterVendasPorCanalCommand(EmpresaId, De, Ate));

        await repo.Received(1).GetVendasPorCanalAsync(EmpresaId, DeUtc, AteUtc, Arg.Any<Guid?>());
    }
}
