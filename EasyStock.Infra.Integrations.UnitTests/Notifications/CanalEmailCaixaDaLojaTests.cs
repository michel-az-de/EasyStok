using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Atendimento.Email;
using EasyStock.Infra.Notifications.Atendimento;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace EasyStock.Infra.Integrations.UnitTests.Notifications;

/// <summary>
/// #1432: com caixa de suporte configurada, a resposta do console sai por ela, em nome dela, com "Re: assunto" e
/// In-Reply-To do último e-mail recebido; o id externo é o Message-ID. Sem caixa, o e-mail da plataforma de antes.
/// </summary>
public class CanalEmailCaixaDaLojaTests
{
    private const string Contato = "maria@exemplo.com";

    private static readonly CaixaEmailAtendimento Caixa = new(
        "contato@casadababa.com", "Casa da Baba", "imap.hostinger.com", 993, "smtp.hostinger.com", 465,
        "contato@casadababa.com", "segredo-de-teste", DateTime.UtcNow);

    private readonly IEmailService _plataforma = Substitute.For<IEmailService>();
    private readonly ICaixaEmailDoTenant _doTenant = Substitute.For<ICaixaEmailDoTenant>();
    private readonly ICaixaEmailCliente _cliente = Substitute.For<ICaixaEmailCliente>();

    private CanalEmail Sut() => new(_plataforma, _doTenant, _cliente);

    [Fact]
    public async Task ComCaixa_RespondeNoFioPelaCaixaDaLoja()
    {
        _doTenant.ObterCaixaAsync(Arg.Any<CancellationToken>()).Returns(Caixa);
        _doTenant.ObterFioAsync(Contato, Arg.Any<CancellationToken>()).Returns(new FioEmail("Encomenda de bolo", "abc@mail.gmail.com"));
        _cliente.EnviarAsync(Caixa, Arg.Any<EmailSaida>(), Arg.Any<CancellationToken>()).Returns("gerado@casadababa.com");

        var id = await Sut().EnviarTextoAsync(Contato, "Pode ser às 18h.");

        id.Should().Be("gerado@casadababa.com");
        await _cliente.Received(1).EnviarAsync(Caixa,
            new EmailSaida(Contato, "Re: Encomenda de bolo", "Pode ser às 18h.", false, "abc@mail.gmail.com"), Arg.Any<CancellationToken>());
        await _plataforma.DidNotReceiveWithAnyArgs().SendAsync(default(string)!, default!, default!, default);
    }

    [Fact]
    public async Task ComCaixaSemFio_UsaOAssuntoPadraoSemInReplyTo()
    {
        _doTenant.ObterCaixaAsync(Arg.Any<CancellationToken>()).Returns(Caixa);
        _cliente.EnviarAsync(Caixa, Arg.Any<EmailSaida>(), Arg.Any<CancellationToken>()).Returns("x@casadababa.com");

        await Sut().EnviarImagemAsync(Contato, "https://cdn.x/foto.png", "Bolo");

        await _cliente.Received(1).EnviarAsync(Caixa,
            Arg.Is<EmailSaida>(e => e.Assunto == CanalEmail.Assunto && e.EmRespostaA == null && e.Html && e.Corpo.Contains("foto.png")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SemCaixa_UsaOEmailDaPlataformaComoAntes()
    {
        var id = await Sut().EnviarTextoAsync(Contato, "oi");

        id.Should().HaveLength(32);
        await _plataforma.Received(1).SendAsync(Contato, CanalEmail.Assunto, "oi", false);
        await _cliente.DidNotReceiveWithAnyArgs().EnviarAsync(default!, default!, default);
    }

    [Fact]
    public async Task FalhaDaCaixa_ViraFalhaDeEnvioDoCanal_ComOTipo()
    {
        _doTenant.ObterCaixaAsync(Arg.Any<CancellationToken>()).Returns(Caixa);
        _cliente.EnviarAsync(Caixa, Arg.Any<EmailSaida>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new FalhaCaixaEmailException("SMTP: usuário ou senha recusados", permanente: true));

        var act = () => Sut().EnviarTextoAsync(Contato, "oi");

        var falha = (await act.Should().ThrowAsync<EnvioCanalFalhouException>()).Which;
        falha.FalhaPermanente.Should().BeTrue();
        falha.Message.Should().Contain("usuário ou senha recusados");
    }
}
