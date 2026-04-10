using EasyStock.Domain.Defaults;
using FluentAssertions;

namespace EasyStock.Domain.Tests.Defaults;

public class OperacionalDefaultsTests
{
    [Fact]
    public void DiasAlertaValidade_deve_ser_15() =>
        OperacionalDefaults.DiasAlertaValidade.Should().Be(15);

    [Fact]
    public void DiasAlertaParado_deve_ser_30() =>
        OperacionalDefaults.DiasAlertaParado.Should().Be(30);

    [Fact]
    public void QuantidadeMinima_deve_ser_5() =>
        OperacionalDefaults.QuantidadeMinima.Should().Be(5);

    [Fact]
    public void Moeda_deve_ser_BRL() =>
        OperacionalDefaults.Moeda.Should().Be("BRL");

    [Fact]
    public void Timezone_deve_ser_America_Sao_Paulo() =>
        OperacionalDefaults.Timezone.Should().Be("America/Sao_Paulo");
}
