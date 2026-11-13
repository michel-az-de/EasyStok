using EasyStock.Api.Startup;
using FluentAssertions;

namespace EasyStock.Api.UnitTests.Startup;

/// <summary>#2072: com a ponte do WAHA ligada, a API não sobe sem endereço, chave, segredo do HMAC e número da loja.</summary>
public class StartupHardeningWahaTests
{
    private const string Segredo = "segredo-hmac-de-teste-com-mais-de-32-caracteres";

    [Fact]
    public void PonteCompletaSobe()
    {
        var act = () => StartupHardening.ValidateWhatsAppWaha("waha", "http://ez-waha:3000", "chave", Segredo, "5541999990000");

        act.Should().NotThrow();
    }

    [Fact]
    public void PonteDesligadaNaoExigeNada()
    {
        var act = () => StartupHardening.ValidateWhatsAppWaha("meta", null, null, null, null);

        act.Should().NotThrow();
    }

    [Theory]
    [InlineData(null, "chave", Segredo, "5541999990000", "*BaseUrl*")]
    [InlineData("ez-waha:3000/x y", "chave", Segredo, "5541999990000", "*BaseUrl*")]
    [InlineData("http://ez-waha:3000", "", Segredo, "5541999990000", "*ApiKey*")]
    [InlineData("http://ez-waha:3000", "chave", "curto", "5541999990000", "*HmacKey*")]
    [InlineData("http://ez-waha:3000", "chave", Segredo, "", "*PhoneNumberId*")]
    [InlineData("http://ez-waha:3000", "chave", Segredo, "99990000", "*PhoneNumberId*")]
    public void PonteIncompletaNaoSobe(string? baseUrl, string? apiKey, string? hmacKey, string? numero, string mensagem)
    {
        var act = () => StartupHardening.ValidateWhatsAppWaha("waha", baseUrl, apiKey, hmacKey, numero);

        act.Should().Throw<InvalidOperationException>().WithMessage(mensagem);
    }
}
