using EasyStock.Application.Ports.Output.Atendimento.Email;
using EasyStock.Infra.Async.Email.Atendimento;
using FluentAssertions;
using MailKit.Security;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyStock.Infra.Async.UnitTests.Email.Atendimento;

/// <summary>
/// #1432: a resposta da loja sai pelo SMTP da caixa dela, em nome dela, encadeada no fio do cliente. Contra o SMTP
/// falso em loopback da N3 (<see cref="ServidorSmtpDeTeste"/>), com STARTTLS e AUTH.
/// </summary>
public class CaixaEmailMailKitTests
{
    private const string Senha = "senha-da-caixa-de-teste";

    private static CaixaEmailAtendimento Caixa(ServidorSmtpDeTeste smtp) => new(
        "contato@casadababa.com", "Casa da Baba", "127.0.0.1", 1, "127.0.0.1", smtp.Porta,
        "contato@casadababa.com", Senha, DateTime.UtcNow);

    private static CaixaEmailMailKit Sut() =>
        new(NullLogger<CaixaEmailMailKit>.Instance, TimeProvider.System, (_, _, _, _) => true);

    [Fact]
    public async Task Envia_EmNomeDaCaixa_ComReEInReplyTo_EDevolveOMessageId()
    {
        await using var smtp = new ServidorSmtpDeTeste
        {
            Tls = ServidorSmtpDeTeste.ModoTls.StartTls, AnunciaAuth = true, ExigeAuth = true,
        }.Iniciar();

        var id = await Sut().EnviarAsync(Caixa(smtp),
            new EmailSaida("maria@exemplo.com", "Re: Encomenda de bolo", "Pode ser às 18h.", Html: false, "<abc123@mail.gmail.com>"));

        var recebida = smtp.Mensagens.Should().ContainSingle().Subject;
        recebida.UsuarioAutenticado.Should().Be("contato@casadababa.com");
        recebida.Remetente.Should().Be("contato@casadababa.com");
        recebida.Destinatarios.Should().Equal("maria@exemplo.com");
        id.Should().EndWith("@casadababa.com");
        recebida.Bruto.Should().Contain($"Message-Id: <{id}>")
            .And.Contain("In-Reply-To: <abc123@mail.gmail.com>")
            .And.Contain("References: <abc123@mail.gmail.com>")
            .And.Contain("From: Casa da Baba <contato@casadababa.com>")
            .And.Contain("Subject: Re: Encomenda de bolo")
            .And.Contain("Pode ser")
            .And.NotContain("Auto-Submitted", "é resposta humana, não aviso automático");
    }

    [Fact]
    public async Task LoginRecusado_FalhaPermanente_SemASenhaNaMensagem()
    {
        await using var smtp = new ServidorSmtpDeTeste
        {
            Tls = ServidorSmtpDeTeste.ModoTls.StartTls, AnunciaAuth = true, RespostaAuth = "535 5.7.8 Autenticacao recusada",
        }.Iniciar();

        var act = () => Sut().EnviarAsync(Caixa(smtp), new EmailSaida("maria@exemplo.com", "Re: x", "oi", false, null));

        var falha = (await act.Should().ThrowAsync<FalhaCaixaEmailException>()).Which;
        falha.Permanente.Should().BeTrue();
        falha.Message.Should().Contain("usuário ou senha recusados").And.NotContain(Senha);
        smtp.Mensagens.Should().BeEmpty();
    }

    [Fact]
    public async Task Testar_ImapForaDoAr_ESmtpOk_DizCadaLado()
    {
        await using var smtp = new ServidorSmtpDeTeste
        {
            Tls = ServidorSmtpDeTeste.ModoTls.StartTls, AnunciaAuth = true, ExigeAuth = true,
        }.Iniciar();

        var r = await Sut().TestarAsync(Caixa(smtp)); // IMAP na porta 1: ninguém escutando

        r.SmtpOk.Should().BeTrue();
        r.ImapOk.Should().BeFalse();
        r.ImapErro.Should().StartWith("IMAP:").And.NotContain(Senha);
        r.Ok.Should().BeFalse();
        smtp.Mensagens.Should().BeEmpty("testar não envia nada");
    }

    [Theory]
    [InlineData(993, SecureSocketOptions.SslOnConnect)]
    [InlineData(465, SecureSocketOptions.SslOnConnect)]
    [InlineData(587, SecureSocketOptions.StartTls)]
    [InlineData(143, SecureSocketOptions.StartTls)]
    public void SegurancaPelaPorta_NuncaTextoPuro(int porta, SecureSocketOptions esperado) =>
        CaixaEmailMailKit.SegurancaDaPorta(porta).Should().Be(esperado);

    [Fact]
    public void DestinatarioInvalido_FalhaPermanenteAntesDeConectar()
    {
        var caixa = new CaixaEmailAtendimento("contato@casadababa.com", null, "i", 993, "s", 465, "u", Senha, DateTime.UtcNow);

        var act = () => CaixaEmailMailKit.Montar(caixa, new EmailSaida("sem-arroba", "Re: x", "oi", false, null));

        act.Should().Throw<FalhaCaixaEmailException>().Which.Permanente.Should().BeTrue();
    }
}
