using EasyStock.Web.Helpers;
using FluentAssertions;

namespace EasyStock.Web.UnitTests.Helpers;

// EST-01: texto amigavel do badge de validade — corrige o "-2361 d" cru do QA.
public class FormatHelperTests
{
    [Theory]
    [InlineData(-2361, "vencido há 2361 dias")]
    [InlineData(-1, "vencido há 1 dia")]
    [InlineData(0, "vence hoje")]
    [InlineData(1, "1 dia")]
    [InlineData(5, "5 dias")]
    public void AsValidadeBadge_formata_por_faixa(int dias, string esperado)
    {
        dias.AsValidadeBadge().Should().Be(esperado);
    }

    [Fact]
    public void AsValidadeBadge_nullable_vazio_quando_null()
    {
        ((int?)null).AsValidadeBadge().Should().Be("");
        ((int?)-3).AsValidadeBadge().Should().Be("vencido há 3 dias");
    }
}
