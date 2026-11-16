using EasyStock.Web.Helpers;
using FluentAssertions;

namespace EasyStock.Web.UnitTests.Helpers;

// #2747 WR1-01: cada vencimento sai da primeira data, nao da parcela anterior.
public class ParcelasHelperTests
{
    [Fact]
    public void Mensal_a_partir_do_dia_31_preserva_o_fim_do_mes()
    {
        var parcelas = ParcelasHelper.Montar(400m, 4, new DateTime(2026, 1, 31), "mensal");

        parcelas.Select(p => p.DataVencimento).Should().Equal(
            new DateTime(2026, 1, 31), new DateTime(2026, 2, 28),
            new DateTime(2026, 3, 31), new DateTime(2026, 4, 30));
    }

    [Fact]
    public void Quinzenal_soma_15_dias_sobre_a_primeira()
    {
        var parcelas = ParcelasHelper.Montar(300m, 3, new DateTime(2026, 1, 31), "quinzenal");

        parcelas.Select(p => p.DataVencimento).Should().Equal(
            new DateTime(2026, 1, 31), new DateTime(2026, 2, 15), new DateTime(2026, 3, 2));
    }

    [Fact]
    public void Semanal_soma_7_dias_sobre_a_primeira()
    {
        var parcelas = ParcelasHelper.Montar(300m, 3, new DateTime(2026, 1, 31), "semanal");

        parcelas.Select(p => p.DataVencimento).Should().Equal(
            new DateTime(2026, 1, 31), new DateTime(2026, 2, 7), new DateTime(2026, 2, 14));
    }

    [Fact]
    public void Residuo_da_divisao_vai_para_a_ultima_parcela()
    {
        var parcelas = ParcelasHelper.Montar(100m, 3, new DateTime(2026, 1, 10), "mensal");

        parcelas.Select(p => p.Valor).Should().Equal(33.33m, 33.33m, 33.34m);
        parcelas.Sum(p => p.Valor).Should().Be(100m);
        parcelas.Select(p => p.Numero).Should().Equal(1, 2, 3);
    }
}
