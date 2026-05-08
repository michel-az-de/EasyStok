using EasyStock.Domain.ValueObjects.Fiscal;
using FluentAssertions;

namespace EasyStock.Domain.Tests.ValueObjects.Fiscal;

public class NCMTests
{
    [Fact]
    public void Parse_aceita_8_digitos()
    {
        NCM.Parse("19059020").Valor.Should().Be("19059020");
    }

    [Fact]
    public void Parse_remove_separadores_e_aceita()
    {
        NCM.Parse("1905.90.20").Valor.Should().Be("19059020");
    }

    [Fact]
    public void Parse_com_menos_de_8_lanca_exception()
    {
        var act = () => NCM.Parse("1234567");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Parse_com_mais_de_8_lanca_exception()
    {
        var act = () => NCM.Parse("123456789");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Parse_vazio_lanca_exception()
    {
        var act = () => NCM.Parse("");
        act.Should().Throw<ArgumentException>();
    }
}
