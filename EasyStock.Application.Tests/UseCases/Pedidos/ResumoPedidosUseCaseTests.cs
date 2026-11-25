using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Pedidos;
using Microsoft.Extensions.Time.Testing;

namespace EasyStock.Application.Tests.UseCases.Pedidos;

/// <summary>
/// #3294: o resumo da tela Pedidos. O ticket é a razão das somas; a comparação usa o mesmo trecho do período
/// anterior (para "hoje", ontem até esta hora) e some quando há filtro de entrega ou data final.
/// </summary>
public class ResumoPedidosUseCaseTests
{
    // 24/11/2026 15:00 em Brasília.
    private static readonly DateTime Agora = new(2026, 11, 24, 18, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime InicioDeHoje = new(2026, 11, 24, 3, 0, 0, DateTimeKind.Utc);
    private readonly Guid _empresa = Guid.NewGuid();
    private readonly IPedidosResumoQuery _consulta = Substitute.For<IPedidosResumoQuery>();

    private ResumoPedidosUseCase Caso() => new(_consulta, new FakeTimeProvider(new DateTimeOffset(Agora)));

    private static ResumoDosPedidos Resumo(int vendas = 3, decimal vendido = 100m) =>
        new(5, vendas, vendido, 30m, 1, 1, 0, 2, 1, 1, 20m, 1, 2, [new ItemMaisVendido("Bolo", 4m)]);

    [Fact]
    public async Task TicketEARazaoDasSomasEOsBlocosVemDaConsulta()
    {
        _consulta.ResumirAsync(Arg.Any<FiltroDosPedidos>(), Agora, Arg.Any<CancellationToken>()).Returns(Resumo());

        var r = await Caso().ExecuteAsync(new ResumoPedidosQuery(_empresa, Search: "ana"));

        r.TicketMedio.Should().Be(33.33m);
        r.AReceber.Should().Be(new ValorEPedidos(30m, 1));
        r.Cancelados.Should().Be(new ValorEPedidos(20m, 1));
        r.MaisVendidos.Should().ContainSingle(i => i.Nome == "Bolo");
        r.Anterior.Should().BeNull("sem período não há com o que comparar");
        await _consulta.Received(1).ResumirAsync(new FiltroDosPedidos(_empresa, Search: "ana"), Agora, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SemVendaOTicketEZero()
    {
        _consulta.ResumirAsync(Arg.Any<FiltroDosPedidos>(), Agora, Arg.Any<CancellationToken>()).Returns(Resumo(vendas: 0, vendido: 0m));
        (await Caso().ExecuteAsync(new ResumoPedidosQuery(_empresa))).TicketMedio.Should().Be(0m);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(30)]
    public async Task ComparaComOMesmoTrechoDoPeriodoAnterior(int dias)
    {
        var desde = InicioDeHoje.AddDays(1 - dias);
        _consulta.ResumirAsync(Arg.Any<FiltroDosPedidos>(), Agora, Arg.Any<CancellationToken>()).Returns(Resumo());
        _consulta.VendasAsync(Arg.Any<FiltroDosPedidos>(), Arg.Any<CancellationToken>()).Returns(new VendasDoPeriodo(2, 80m));

        var r = await Caso().ExecuteAsync(new ResumoPedidosQuery(_empresa, Desde: desde, Search: "ana"));

        r.Anterior.Should().Be(new PeriodoAnteriorResult(desde.AddDays(-dias), Agora.AddDays(-dias), 2, 80m));
        await _consulta.Received(1).VendasAsync(new FiltroDosPedidos(_empresa, Desde: desde.AddDays(-dias), Ate: Agora.AddDays(-dias), Search: "ana"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void SemComparacaoComFiltroDeEntregaOuDataFinal()
    {
        var desde = InicioDeHoje.AddDays(-6);
        ResumoPedidosUseCase.JanelaAnterior(new ResumoPedidosQuery(_empresa, Desde: desde, EntregaDesde: InicioDeHoje), Agora).Should().BeNull();
        ResumoPedidosUseCase.JanelaAnterior(new ResumoPedidosQuery(_empresa, Desde: desde, Ate: Agora), Agora).Should().BeNull();
        ResumoPedidosUseCase.JanelaAnterior(new ResumoPedidosQuery(_empresa, Desde: Agora.AddHours(1)), Agora).Should().BeNull();
    }

    [Fact]
    public async Task EmpresaVaziaRecusa()
    {
        var acao = () => Caso().ExecuteAsync(new ResumoPedidosQuery(Guid.Empty));
        await acao.Should().ThrowAsync<Exception>();
        await _consulta.DidNotReceiveWithAnyArgs().ResumirAsync(default!, default, default);
    }
}
