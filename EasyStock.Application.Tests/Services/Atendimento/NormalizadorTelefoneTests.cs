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
    [InlineData("5511997573992", "+5511997573992")] // #1290: como o VO Telefone grava o "55..." digitado sem '+'
    [InlineData("551133334444", "+551133334444")]
    public void NormalizarE164BrAceitaFormatosComuns(string entrada, string esperado) =>
        NormalizadorTelefone.NormalizarE164Br(entrada).Should().Be(esperado);

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("551199757399212")] // 55 + 13 dígitos: nem nacional nem E.164 BR
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

    [Theory]
    [InlineData("551197573992", new[] { "551197573992", "5511997573992" })] // wa_id antigo, sem o 9
    [InlineData("5511997573992", new[] { "5511997573992", "551197573992" })] // cadastro, com o 9
    [InlineData("551133334444", new[] { "551133334444" })] // fixo: não tem nono dígito
    [InlineData("14155550100", new[] { "14155550100" })]
    public void VariantesContatoWhatsAppCobremAsDuasGrafiasDoNonoDigito(string contato, string[] esperado) =>
        NormalizadorTelefone.VariantesContatoWhatsApp(contato).Should().Equal(esperado);

    [Fact]
    public void CandidatosDeWaIdEstrangeiroPreservaNumero() =>
        NormalizadorTelefone.CandidatosDeWaId("14155550100")
            .Should().Equal("+14155550100");
}
