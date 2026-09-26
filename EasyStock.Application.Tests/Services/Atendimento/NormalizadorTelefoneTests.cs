using EasyStock.Application.Services.Atendimento;
using EasyStock.Domain.Exceptions.Storefront;

namespace EasyStock.Application.Tests.Services.Atendimento;

public class NormalizadorTelefoneTests
{
    [Theory]
    [InlineData("+5511997573992", "+5511997573992")]
    [InlineData("(11) 99757-3992", "+5511997573992")]
    [InlineData("11 99757-3992", "+5511997573992")]
    [InlineData("+55 11 9 9757 3992", "+5511997573992")]
    [InlineData("1133334444", "+551133334444")]
    public void NormalizarE164BrAceitaFormatosComuns(string entrada, string esperado) =>
        NormalizadorTelefone.NormalizarE164Br(entrada).Should().Be(esperado);

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("5511997573992")] // sem '+' e com 13 dígitos: o OTP sempre recusou
    [InlineData("+1 415 555 0100")]
    public void NormalizarE164BrRecusaInvalido(string entrada) =>
        FluentActions.Invoking(() => NormalizadorTelefone.NormalizarE164Br(entrada))
            .Should().Throw<TelefoneInvalidoException>();

    [Fact]
    public void CandidatosDeWaIdComNonoDigito() =>
        NormalizadorTelefone.CandidatosDeWaId("5511997573992")
            .Should().Equal("+5511997573992");

    [Fact]
    public void CandidatosDeWaIdSemNonoDigitoPriorizaFormaCanonica() =>
        NormalizadorTelefone.CandidatosDeWaId("551197573992")
            .Should().Equal("+5511997573992", "+551197573992");

    [Fact]
    public void CandidatosDeWaIdFixoNaoGanhaNono() =>
        NormalizadorTelefone.CandidatosDeWaId("551133334444")
            .Should().Equal("+551133334444");

    [Fact]
    public void CandidatosDeWaIdEstrangeiroPreservaNumero() =>
        NormalizadorTelefone.CandidatosDeWaId("14155550100")
            .Should().Equal("+14155550100");
}
