using EasyStock.Api.Startup;
using FluentAssertions;

namespace EasyStock.Api.UnitTests.Startup;

public class StartupHardeningTests
{
    [Fact]
    public void MetaSemAppSecretFalha()
    {
        var act = () => StartupHardening.ValidateWhatsAppMetaCore(
            accessToken: "token-valido", appSecret: "", verifyToken: "verify-valido");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Notifications:WhatsApp:Meta:AppSecret*");
    }

    [Fact]
    public void MetaSemAccessTokenFalha()
    {
        var act = () => StartupHardening.ValidateWhatsAppMetaCore(
            accessToken: "", appSecret: "secret-valido", verifyToken: "verify-valido");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Notifications:WhatsApp:Meta:AccessToken*");
    }

    [Fact]
    public void MetaSemVerifyTokenFalha()
    {
        var act = () => StartupHardening.ValidateWhatsAppMetaCore(
            accessToken: "token-valido", appSecret: "secret-valido", verifyToken: "");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Notifications:WhatsApp:Meta:VerifyToken*");
    }

    [Fact]
    public void MetaComTudoPreenchidoNaoFalha()
    {
        var act = () => StartupHardening.ValidateWhatsAppMetaCore(
            accessToken: "token-valido", appSecret: "secret-valido", verifyToken: "verify-valido");

        act.Should().NotThrow();
    }

    [Fact]
    public void ProviderDiferenteDeMetaNaoValida()
    {
        var act = () => StartupHardening.ValidateWhatsAppMeta(provider: "stub",
            accessToken: "", appSecret: "", verifyToken: "");

        act.Should().NotThrow();
    }

    [Fact]
    public void ClienteDoAtendimentoMetaComProviderStubExigeCredenciais()
    {
        var act = () => StartupHardening.ValidateWhatsAppMeta(clienteAtendimento: "meta", provider: "stub",
            accessToken: "", appSecret: "secret-valido", verifyToken: "verify-valido");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Notifications:WhatsApp:Meta:AccessToken*Atendimento:WhatsApp:Cliente=meta*");
    }

    [Fact]
    public void ClienteDoAtendimentoMetaNaoExigePhoneNumberIdGlobal()
    {
        // O phone_number_id vem da empresa do tenant (#1102); o global é só fallback.
        var act = () => StartupHardening.ValidateWhatsAppMeta(clienteAtendimento: "meta", provider: "stub",
            accessToken: "token-valido", appSecret: "secret-valido", verifyToken: "verify-valido");

        act.Should().NotThrow();
    }

    [Fact]
    public void ClienteEProviderStubNaoValidam()
    {
        var act = () => StartupHardening.ValidateWhatsAppMeta(clienteAtendimento: "stub", provider: "stub",
            accessToken: "", appSecret: "", verifyToken: "");

        act.Should().NotThrow();
    }

    // N6: o WhatsApp de plataforma conta como "usa a Meta" e exige o número e o verify token próprios.

    [Theory]
    [InlineData("", "verify", "token", "secret", "PhoneNumberId")]
    [InlineData("abc", "verify", "token", "secret", "PhoneNumberId")]
    [InlineData("123456789012345678901234567890123", "verify", "token", "secret", "PhoneNumberId")]
    [InlineData("7770009999", "", "token", "secret", "Plataforma:VerifyToken")]
    [InlineData("7770009999", "verify", "", "secret", "Meta:AccessToken")]
    [InlineData("7770009999", "verify", "token", "", "Meta:AppSecret")]
    public void PlataformaMetaExigePhoneNumberIdEVerifyToken(
        string numero, string verify, string token, string secret, string chaveEsperada)
    {
        var act = () => StartupHardening.ValidateWhatsAppPlataforma("meta", numero, verify, token, secret);

        act.Should().Throw<InvalidOperationException>().WithMessage($"*{chaveEsperada}*");
    }

    [Fact]
    public void PlataformaMetaCompletaNaoFalha()
    {
        var act = () => StartupHardening.ValidateWhatsAppPlataforma("meta", "7770009999", "verify", "token", "secret");

        act.Should().NotThrow();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("stub")]
    public void PlataformaStubNaoExigeNada(string? provider)
    {
        var act = () => StartupHardening.ValidateWhatsAppPlataforma(provider, "", "", "", "");

        act.Should().NotThrow();
    }
}
