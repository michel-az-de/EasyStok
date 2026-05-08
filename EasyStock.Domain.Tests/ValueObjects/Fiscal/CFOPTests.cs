using EasyStock.Domain.ValueObjects.Fiscal;
using FluentAssertions;

namespace EasyStock.Domain.Tests.ValueObjects.Fiscal;

public class CFOPTests
{
    [Theory]
    [InlineData("5102", true, false, false)]
    [InlineData("6102", false, true, false)]
    [InlineData("7102", false, false, true)]
    public void Parse_classifica_corretamente_pela_primeira_digito(
        string raw, bool intra, bool inter, bool exterior)
    {
        var cfop = CFOP.Parse(raw);

        cfop.DentroDoEstado.Should().Be(intra);
        cfop.ForaDoEstado.Should().Be(inter);
        cfop.ParaExterior.Should().Be(exterior);
    }

    [Fact]
    public void Parse_com_3_digitos_lanca_exception()
    {
        var act = () => CFOP.Parse("510");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Parse_iniciando_com_1_lanca_exception_pois_nao_e_saida()
    {
        var act = () => CFOP.Parse("1102");
        act.Should().Throw<ArgumentException>().WithMessage("*5, 6 ou 7*");
    }

    [Fact]
    public void Factories_estaticas_devolvem_cfops_comuns()
    {
        CFOP.VendaIntraEstado().Valor.Should().Be("5102");
        CFOP.VendaInterEstado().Valor.Should().Be("6102");
    }
}
