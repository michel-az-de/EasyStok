using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.Domain.Enums.Notifications;
using EasyStock.Infra.Notifications.Atendimento;
using FluentAssertions;
using NSubstitute;

namespace EasyStock.Infra.Integrations.UnitTests.Notifications;

/// <summary>S37 (ADR-0051): e-mail e SMS entram na porta de canal da S34 como canais de saída.</summary>
public class CanaisEmailSmsTests
{
    [Fact]
    public async Task Sms_DelegaAoProvedorAtivoComMaisNoNumero()
    {
        var provedor = Substitute.For<IProvedorSms>();
        provedor.EnviarAsync(Arg.Any<MensagemPronta>(), Arg.Any<CancellationToken>())
            .Returns(new ResultadoEnvio(true, ProviderUsado: "twilio"));
        var canal = new CanalSms(provedor);

        await canal.EnviarTextoAsync("5511988887777", "Seu pedido saiu!");

        canal.Canal.Should().Be(CanalConversa.Sms);
        await provedor.Received(1).EnviarAsync(
            Arg.Is<MensagemPronta>(m => m.Destinatario == "+5511988887777" && m.Corpo == "Seu pedido saiu!"
                && m.Canal == CanalNotificacao.Sms),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Sms_FalhaDoProvedor_LancaExcecaoTipada()
    {
        var provedor = Substitute.For<IProvedorSms>();
        provedor.EnviarAsync(Arg.Any<MensagemPronta>(), Arg.Any<CancellationToken>())
            .Returns(new ResultadoEnvio(false, ErroDetalhado: "21211 invalid To", FalhaPermanente: true));

        var act = () => new CanalSms(provedor).EnviarTextoAsync("5511988887777", "Oi");

        (await act.Should().ThrowAsync<EnvioCanalFalhouException>())
            .Which.FalhaPermanente.Should().BeTrue();
    }

    [Fact]
    public async Task Sms_BotaoNaoESuportado()
    {
        var act = () => new CanalSms(Substitute.For<IProvedorSms>())
            .EnviarBotoesAsync("5511988887777", "Escolha", [("a", "A")]);

        await act.Should().ThrowAsync<NotSupportedException>();
    }

    [Fact]
    public async Task Email_TextoVaiPeloEmailService()
    {
        var email = Substitute.For<IEmailService>();
        var canal = new CanalEmail(email);

        await canal.EnviarTextoAsync("fulana@exemplo.com", "Lembrete: seu pedido é amanhã.");

        canal.Canal.Should().Be(CanalConversa.Email);
        await email.Received(1).SendAsync("fulana@exemplo.com", Arg.Any<string>(), "Lembrete: seu pedido é amanhã.", false);
    }

    [Fact]
    public async Task Email_ImagemVaiComoHtmlComLinkEscapado()
    {
        var email = Substitute.For<IEmailService>();

        await new CanalEmail(email).EnviarImagemAsync("fulana@exemplo.com", "https://cdn.x/cardapio.png?a=1&b=2", "Cardápio <novo>");

        await email.Received(1).SendAsync("fulana@exemplo.com", Arg.Any<string>(),
            Arg.Is<string>(b => b.Contains("src=\"https://cdn.x/cardapio.png?a=1&amp;b=2\"") && b.Contains("&lt;novo&gt;") && !b.Contains("<novo>")),
            true);
    }

    [Fact]
    public async Task Email_ModeloNaoESuportado()
    {
        var act = () => new CanalEmail(Substitute.For<IEmailService>())
            .EnviarModeloAsync("fulana@exemplo.com", "pedido_pago", "pt_BR", []);

        await act.Should().ThrowAsync<NotSupportedException>();
    }
}
