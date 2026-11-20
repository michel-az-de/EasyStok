using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Analytics.PedidosPorAnuncio;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.Tests.UseCases.Analytics;

/// <summary>#2928 (S5): rótulos, ticket médio e ordem do relatório de pedidos por anúncio.</summary>
public class ObterPedidosPorAnuncioUseCaseTests
{
    private readonly IAnalyticsRepository _repository = Substitute.For<IAnalyticsRepository>();

    private ObterPedidosPorAnuncioUseCase CriarUseCase() =>
        new(_repository, Substitute.For<ILogger<ObterPedidosPorAnuncioUseCase>>());

    [Fact]
    public async Task ExecuteAsync_SemOrigem_ViraLinhaRotuladaEFicaPorUltimo()
    {
        var empresaId = Guid.NewGuid();
        _repository.GetPedidosPorAnuncioAsync(empresaId, Arg.Any<DateTime>(), Arg.Any<DateTime>()).Returns(
        [
            new PedidoPorAnuncio(null, null, 5, 500m),
            new PedidoPorAnuncio("google", "black-friday", 2, 150m),
            new PedidoPorAnuncio("meta", null, 3, 90m),
        ]);

        var linhas = (await CriarUseCase().ExecuteAsync(new ObterPedidosPorAnuncioCommand(empresaId))).ToList();

        linhas.Select(l => (l.Plataforma, l.Campanha)).Should().Equal(
            ("google", "black-friday"),
            ("meta", ObterPedidosPorAnuncioResult.SemCampanha),
            (ObterPedidosPorAnuncioResult.PlataformaSemOrigem, ObterPedidosPorAnuncioResult.RotuloSemOrigem));
        linhas[0].TicketMedio.Should().Be(75m);
        linhas[2].TotalPedidos.Should().Be(5);
        linhas[2].TicketMedio.Should().Be(100m);
    }

    [Fact]
    public async Task ExecuteAsync_DataPura_UsaODiaInteiroDeBrasilia()
    {
        var empresaId = Guid.NewGuid();
        _repository.GetPedidosPorAnuncioAsync(empresaId, Arg.Any<DateTime>(), Arg.Any<DateTime>()).Returns([]);

        await CriarUseCase().ExecuteAsync(new ObterPedidosPorAnuncioCommand(
            empresaId, new DateTime(2026, 11, 1), new DateTime(2026, 11, 30)));

        await _repository.Received(1).GetPedidosPorAnuncioAsync(
            empresaId,
            new DateTime(2026, 11, 1, 3, 0, 0, DateTimeKind.Utc),
            Arg.Is<DateTime>(ate => ate >= new DateTime(2026, 12, 1, 2, 59, 59, DateTimeKind.Utc) && ate < new DateTime(2026, 12, 1, 3, 0, 0, DateTimeKind.Utc)));
    }

    [Fact]
    public async Task ExecuteAsync_EmpresaVazia_Recusa()
    {
        var acao = () => CriarUseCase().ExecuteAsync(new ObterPedidosPorAnuncioCommand(Guid.Empty));
        await acao.Should().ThrowAsync<Exception>();
    }
}
