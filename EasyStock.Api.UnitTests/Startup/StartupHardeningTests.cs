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
}
