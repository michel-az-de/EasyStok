using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Domain.Enums.Notifications;
using EasyStock.Infra.Notifications.WhatsApp;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace EasyStock.Infra.Integrations.UnitTests.Notifications;

/// <summary>N6: o remetente é escolhido por mensagem, pela chave do override, e a plataforma nunca cai no número da loja.</summary>
public class WhatsAppCanalTests
{
    private readonly IProvedorWhatsApp _loja = Substitute.For<IProvedorWhatsApp>();
    private readonly IProvedorWhatsApp _plataforma = Substitute.For<IProvedorWhatsApp>();

    public WhatsAppCanalTests()
    {
        _loja.Nome.Returns("meta");
        _plataforma.Nome.Returns("meta-plataforma");
        _loja.EnviarAsync(Arg.Any<MensagemPronta>(), Arg.Any<CancellationToken>())
            .Returns(new ResultadoEnvio(true, "meta"));
        _plataforma.EnviarAsync(Arg.Any<MensagemPronta>(), Arg.Any<CancellationToken>())
            .Returns(new ResultadoEnvio(true, "meta-plataforma"));
    }

    private WhatsAppCanal Canal(bool comPlataforma)
    {
        var services = new ServiceCollection();
        services.AddKeyedSingleton("whatsapp:active", _loja);
        if (comPlataforma) services.AddKeyedSingleton("whatsapp:plataforma", _plataforma);
        var sp = services.BuildServiceProvider();
        return new WhatsAppCanal(sp.GetRequiredKeyedService<IProvedorWhatsApp>("whatsapp:active"), sp,
            NullLogger<WhatsAppCanal>.Instance);
    }

    private static MensagemPronta Mensagem(string? providerOverride) => new(
        Guid.NewGuid(), Guid.NewGuid(), "+5511999990001", "", "corpo", CanalNotificacao.WhatsApp,
        CategoriaConteudoNotificacao.Seguranca, providerOverride);

    [Fact]
    public async Task SemOverrideUsaOProviderDaLoja()
    {
        var r = await Canal(comPlataforma: true).EnviarAsync(Mensagem(null));

        r.ProviderUsado.Should().Be("meta");
        await _plataforma.DidNotReceive().EnviarAsync(Arg.Any<MensagemPronta>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OverrideDePlataformaEscolheOProviderDePlataforma()
    {
        var r = await Canal(comPlataforma: true).EnviarAsync(Mensagem("plataforma"));

        r.ProviderUsado.Should().Be("meta-plataforma");
        await _loja.DidNotReceive().EnviarAsync(Arg.Any<MensagemPronta>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("plataforma", false)]
    [InlineData("desconhecido", true)]
    public async Task OverrideSemProviderFalhaPermanenteSemCairNoDaLoja(string chave, bool comPlataforma)
    {
        var r = await Canal(comPlataforma).EnviarAsync(Mensagem(chave));

        r.Sucesso.Should().BeFalse();
        r.FalhaPermanente.Should().BeTrue();
        r.Desfecho.Should().Be(DesfechoEnvio.FalhaPermanente);
        r.ErroDetalhado.Should().Be(WhatsAppCanal.ErroProviderNaoConfigurado);
        await _loja.DidNotReceive().EnviarAsync(Arg.Any<MensagemPronta>(), Arg.Any<CancellationToken>());
    }
}
