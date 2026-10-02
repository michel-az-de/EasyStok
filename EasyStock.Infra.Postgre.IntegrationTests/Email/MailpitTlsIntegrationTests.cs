using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Infra.Async;
using EasyStock.Infra.Async.Email;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyStock.Infra.Postgre.IntegrationTests.Email;

/// <summary>
/// N3 (#1351): o transporte seguro do MailKit contra um Mailpit de verdade com certificado proprio. Os dois modos que a
/// porta decide (465 implicito, o resto STARTTLS) entregam com a credencial, e o certificado autoassinado so passa
/// quando o teste injeta o validador (o servico, por padrao, recusa).
/// </summary>
public class MailpitTlsIntegrationTests(MailpitStartTlsFixture startTls, MailpitTlsImplicitoFixture tlsImplicito)
    : IClassFixture<MailpitStartTlsFixture>, IClassFixture<MailpitTlsImplicitoFixture>
{
    private const string Usuario = "avisos@easystok.online";

    private static SmtpEmailService Servico(MailpitFixture mailpit, SmtpModo modo, bool aceitarQualquerCertificado)
    {
        var configuracao = new SmtpOpcoes
        {
            Host = mailpit.Host,
            Port = mailpit.PortaSmtp.ToString(),
            Modo = modo.ToString(),
            Username = Usuario,
            Password = "qualquer-senha-o-mailpit-aceita-qualquer-uma",
            FromEmail = Usuario,
            TimeoutSegundos = "10",
        }.Resolver("Production");

        return new SmtpEmailService(
            configuracao,
            NullLogger<SmtpEmailService>.Instance,
            aceitarQualquerCertificado ? (_, _, _, _) => true : null);
    }

    [SkippableFact]
    public async Task StartTlsEntregaComCredencialNoMailpit()
    {
        Skip.If(!startTls.IsAvailable, startTls.UnavailableReason ?? "Docker/Mailpit indisponivel");
        await startTls.LimparAsync();

        var resultado = await Servico(startTls, SmtpModo.StartTls, aceitarQualquerCertificado: true)
            .EnviarAsync(new MensagemEmail("maria.souza@example.com", "STARTTLS", "<p>ok</p>", Html: true));

        resultado.Desfecho.Should().Be(DesfechoEnvio.Enviado);
        var mensagem = (await startTls.AguardarAsync(1)).Single();
        mensagem.DeEndereco.Should().Be(Usuario);
        mensagem.UsuarioAutenticado.Should().Be(Usuario, "o AUTH foi feito, e so depois do TLS");
    }

    [SkippableFact]
    public async Task TlsImplicitoEntregaComCredencialNoMailpit()
    {
        Skip.If(!tlsImplicito.IsAvailable, tlsImplicito.UnavailableReason ?? "Docker/Mailpit indisponivel");
        await tlsImplicito.LimparAsync();

        var resultado = await Servico(tlsImplicito, SmtpModo.SslImplicito, aceitarQualquerCertificado: true)
            .EnviarAsync(new MensagemEmail("maria.souza@example.com", "SMTPS", "<p>ok</p>", Html: true));

        resultado.Desfecho.Should().Be(DesfechoEnvio.Enviado);
        var mensagem = (await tlsImplicito.AguardarAsync(1)).Single();
        mensagem.DeEndereco.Should().Be(Usuario);
        mensagem.UsuarioAutenticado.Should().Be(Usuario);
    }

    [SkippableFact]
    public async Task CertificadoAutoassinadoERecusadoQuandoOValidadorNaoEInjetado()
    {
        Skip.If(!startTls.IsAvailable, startTls.UnavailableReason ?? "Docker/Mailpit indisponivel");
        await startTls.LimparAsync();

        var resultado = await Servico(startTls, SmtpModo.StartTls, aceitarQualquerCertificado: false)
            .EnviarAsync(new MensagemEmail("maria.souza@example.com", "Sem validador", "<p>ok</p>", Html: true));

        resultado.Desfecho.Should().Be(DesfechoEnvio.FalhaTransitoria);
        (await startTls.ContarAsync()).Should().Be(0, "o servico nao afrouxa a validacao do certificado sozinho");
    }
}
