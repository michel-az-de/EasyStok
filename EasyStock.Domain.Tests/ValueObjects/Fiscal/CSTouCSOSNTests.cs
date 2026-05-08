using EasyStock.Domain.ValueObjects.Fiscal;
using FluentAssertions;

namespace EasyStock.Domain.Tests.ValueObjects.Fiscal;

public class CSTouCSOSNTests
{
    [Fact]
    public void ParaSimples_aceita_3_digitos()
    {
        var v = CSTouCSOSN.ParaSimples("102");
        v.ESimplesNacional.Should().BeTrue();
        v.Valor.Should().Be("102");
    }

    [Fact]
    public void ParaSimples_aceita_4_digitos()
    {
        var v = CSTouCSOSN.ParaSimples("0102");
        v.ESimplesNacional.Should().BeTrue();
    }

    [Fact]
    public void ParaRegimeNormal_aceita_2_digitos()
    {
        var v = CSTouCSOSN.ParaRegimeNormal("00");
        v.ESimplesNacional.Should().BeFalse();
        v.Valor.Should().Be("00");
    }

    [Fact]
    public void ParaSimples_com_2_digitos_lanca_exception()
    {
        var act = () => CSTouCSOSN.ParaSimples("10");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ParaRegimeNormal_com_4_digitos_lanca_exception()
    {
        var act = () => CSTouCSOSN.ParaRegimeNormal("0102");
        act.Should().Throw<ArgumentException>();
    }
}
