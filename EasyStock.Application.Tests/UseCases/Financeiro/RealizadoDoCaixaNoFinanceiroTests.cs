using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Caixa;
using EasyStock.Application.UseCases.Financeiro.Dashboard;

namespace EasyStock.Application.Tests.UseCases.Financeiro;

/// <summary>
/// #2268: o realizado dos Relatórios e da visão geral do Financeiro soma o que o Caixa soma fora das contas (pagamento
/// de pedido, venda, estorno de venda, movimento avulso), como o Extrato.
/// </summary>
public class RealizadoDoCaixaNoFinanceiroTests
{
    private static readonly Guid Empresa = Guid.NewGuid();
    // 01/06 e 02/06 de Brasília: dia civil 00:00Z na linha, meia-noite real 03:00Z.
    private static readonly DateTime Dia1 = new(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Dia1Real = Dia1.AddHours(3);

    private readonly IFluxoCaixaQueries _queries = Substitute.For<IFluxoCaixaQueries>();
    private readonly ICaixaSaldoCalculator _calc = Substitute.For<ICaixaSaldoCalculator>();

    public RealizadoDoCaixaNoFinanceiroTests()
    {
        _calc.GetLinhasExtrasAsync(default, default, default).ReturnsForAnyArgs((IReadOnlyList<CaixaLinhaExtraResult>)
        [
            new(Dia1Real.AddHours(10), "pagamento", 19m, "Pagamento de pedido", "pix", "Pedido"),
            new(Dia1Real.AddDays(1).AddHours(1), "venda", 30m, null, "dinheiro", "Venda"),
            new(Dia1Real.AddDays(1).AddHours(2), "estorno", -5m, "Estorno de venda", "dinheiro", "Venda"),
            // 01/06 23:30 de Brasília = 02/06 02:30Z: ainda é o dia 1.
            new(Dia1Real.AddHours(23.5), "pagamento", 1m, "Pagamento de pedido", "pix", "Pedido"),
        ]);
        _queries.MovimentosAvulsosDoCaixaAsync(default, default, default).ReturnsForAnyArgs((IReadOnlyList<RealizadoNoCaixaDto>)
            [new(Dia1Real.AddHours(11), -19m)]);
    }

    private ObterFluxoCaixaUseCase Fluxo() => new(_queries, new RealizadoDoCaixa(_calc, _queries));

    private void DoisDiasComUmaConta() =>
        _queries.FluxoBucketsAsync(default, default, default, default).ReturnsForAnyArgs((IReadOnlyList<FluxoBucketDto>)
        [
            new(Dia1, Dia1.AddDays(1).AddTicks(-1), "01/06", 0m, 0m, 2m, 100m),
            new(Dia1.AddDays(1), Dia1.AddDays(2).AddTicks(-1), "02/06", 0m, 0m, 0m, 0m),
        ]);

    [Fact]
    public async Task Fluxo_soma_pedido_venda_e_movimento_avulso_no_dia_civil_de_Brasilia()
    {
        DoisDiasComUmaConta();

        var linhas = await Fluxo().ExecuteAsync(new(Empresa, PeriodicidadeFluxo.Diario, Dia1, Dia1.AddDays(1)));

        linhas[0].RealizadoReceber.Should().Be(120m, "conta 100 + Pix do pedido 19 + Pix das 23:30 1");
        linhas[0].RealizadoPagar.Should().Be(21m, "conta 2 + devolução 19");
        linhas[0].RealizadoReceberCaixa.Should().Be(20m);
        linhas[0].RealizadoPagarCaixa.Should().Be(19m);
        linhas[1].RealizadoReceber.Should().Be(30m);
        linhas[1].RealizadoPagar.Should().Be(5m, "o estorno da venda sai");
        await _calc.Received(1).GetLinhasExtrasAsync(Empresa, Dia1Real, Dia1Real.AddDays(2));
    }

    [Fact]
    public async Task Fluxo_com_categoria_ou_centro_fica_so_nas_contas()
    {
        DoisDiasComUmaConta();

        var linhas = await Fluxo().ExecuteAsync(new(Empresa, PeriodicidadeFluxo.Diario, Dia1, Dia1.AddDays(1), CategoriaId: Guid.NewGuid()));

        linhas[0].RealizadoReceber.Should().Be(100m);
        linhas[0].RealizadoReceberCaixa.Should().Be(0m);
        await _calc.DidNotReceiveWithAnyArgs().GetLinhasExtrasAsync(default, default, default);
    }

    [Fact]
    public async Task Fluxo_so_no_futuro_nao_consulta_o_caixa()
    {
        var amanha = DateTime.SpecifyKind(DateTime.UtcNow.Date.AddDays(2), DateTimeKind.Utc);
        _queries.FluxoBucketsAsync(default, default, default, default).ReturnsForAnyArgs((IReadOnlyList<FluxoBucketDto>)
            [new(amanha, amanha.AddDays(1).AddTicks(-1), "futuro", 10m, 10m, 0m, 0m)]);

        await Fluxo().ExecuteAsync(new(Empresa, PeriodicidadeFluxo.Diario, amanha, amanha));

        await _calc.DidNotReceiveWithAnyArgs().GetLinhasExtrasAsync(default, default, default);
    }

    [Fact]
    public async Task Visao_geral_soma_o_caixa_do_mes_de_Brasilia()
    {
        _queries.KpisDashboardAsync(default, default).ReturnsForAnyArgs(new DashboardFinanceiroDto(0m, 0m, 0m, 0m, 2m, 100m, 0, 0, 0));
        var uc = new ObterDashboardFinanceiroUseCase(_queries, new RealizadoDoCaixa(_calc, _queries));

        var r = await uc.ExecuteAsync(new(Empresa, Dia1.AddDays(10)));

        r.TotalRecebidoMes.Should().Be(150m, "conta 100 + pedidos 20 + venda 30");
        r.TotalPagoMes.Should().Be(26m, "conta 2 + devolução 19 + estorno 5");
        r.TotalRecebidoMesCaixa.Should().Be(50m);
        r.TotalPagoMesCaixa.Should().Be(24m);
        await _calc.Received(1).GetLinhasExtrasAsync(Empresa, Dia1Real, new DateTime(2026, 7, 1, 3, 0, 0, DateTimeKind.Utc));
    }
}
