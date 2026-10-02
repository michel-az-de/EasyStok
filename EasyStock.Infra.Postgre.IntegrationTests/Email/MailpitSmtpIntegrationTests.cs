using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Domain.Enums.Notifications;
using EasyStock.Infra.Async;
using EasyStock.Infra.Async.Email;
using EasyStock.Infra.Notifications.Email;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyStock.Infra.Postgre.IntegrationTests.Email;

/// <summary>
/// N3 (#1351): o <c>SmtpEmailService</c> real (MailKit) entregando num Mailpit real. O que chega e o que o MailKit de
/// fato mandou: remetente por categoria, HTML integro, cabecalho <c>Auto-Submitted</c> e anexo byte a byte.
/// </summary>
[Collection("Mailpit")]
public class MailpitSmtpIntegrationTests(MailpitFixture mailpit)
{
    private const string Destinatario = "maria.souza@example.com";
    private const string HtmlComAcentos =
        "<h1>Olá, Maria</h1><p>Seu código é <b>123456</b>. Ação concluída: café, pão e açúcar. 100% íntegro.</p>";

    private static readonly char[] SemQuebraFinal = ['\r', '\n'];

    private SmtpEmailService CriarServico()
    {
        var configuracao = new SmtpOpcoes
        {
            Host = mailpit.Host,
            Port = mailpit.PortaSmtp.ToString(),
            Modo = "Nenhum",
            FromEmail = "avisos@easystok.online",
            FromName = "EasyStok Avisos",
            Seguranca = new SmtpRemetenteOpcoes
            {
                FromEmail = "seguranca@easystok.online",
                FromName = "EasyStok Segurança",
            },
        }.Resolver("Development");

        return new SmtpEmailService(configuracao, NullLogger<SmtpEmailService>.Instance);
    }

    private async Task PrepararAsync()
    {
        Skip.If(!mailpit.IsAvailable, mailpit.UnavailableReason ?? "Docker/Mailpit indisponivel");
        await mailpit.LimparAsync();
    }

    [SkippableFact]
    public async Task HtmlChegaComRemetenteDeAvisos()
    {
        await PrepararAsync();

        var resultado = await CriarServico().EnviarAsync(
            new MensagemEmail(Destinatario, "Relatório diário ☕", HtmlComAcentos, Html: true));

        resultado.Desfecho.Should().Be(DesfechoEnvio.Enviado);
        var mensagem = (await mailpit.AguardarAsync(1)).Single();
        mensagem.DeEndereco.Should().Be("avisos@easystok.online");
        mensagem.DeNome.Should().Be("EasyStok Avisos");
        mensagem.Para.Should().Equal(Destinatario);
        mensagem.Assunto.Should().Be("Relatório diário ☕");
        mensagem.Html.TrimEnd(SemQuebraFinal).Should().Be(HtmlComAcentos, "o HTML chega integro, com os acentos");
        mensagem.Cabecalho("Auto-Submitted").Should().Be("auto-generated");
        mensagem.Cabecalho("Message-Id").Should().EndWith("@easystok.online>", "o Message-ID e do dominio do remetente");
    }

    [SkippableFact]
    public async Task SegurancaChegaDeSegurancaComAutoSubmitted()
    {
        await PrepararAsync();

        var resultado = await CriarServico().EnviarAsync(
            new MensagemEmail(Destinatario, "Recuperação de senha", HtmlComAcentos, Html: true, Remetente: RemetenteEmail.Seguranca));

        resultado.Desfecho.Should().Be(DesfechoEnvio.Enviado);
        var mensagem = (await mailpit.AguardarAsync(1)).Single();
        mensagem.DeEndereco.Should().Be("seguranca@easystok.online");
        mensagem.DeNome.Should().Be("EasyStok Segurança");
        mensagem.Cabecalho("Auto-Submitted").Should().Be("auto-generated");
        mensagem.Html.TrimEnd(SemQuebraFinal).Should().Be(HtmlComAcentos);
    }

    [SkippableFact]
    public async Task AnexoChegaIntacto()
    {
        await PrepararAsync();
        var conteudo = new byte[64 * 1024];
        new Random(1351).NextBytes(conteudo);

        // Nome ASCII de proposito: o Mailpit (Go) so decodifica filename RFC 2231 em utf-8 ou us-ascii e deixa de
        // listar o anexo quando o MimeKit usa iso-8859-1 (valido, e todo cliente de e-mail le). O nome com acento
        // esta coberto no teste unitario NomeDeAnexoComAcentoChegaDecodificado.
        var resultado = await CriarServico().EnviarAsync(new MensagemEmail(
            Destinatario, "Relatório em anexo", "<p>Segue o relatório.</p>", Html: true,
            Anexos: [new EmailAttachment("relatorio-diario.pdf", conteudo, "application/pdf")]));

        resultado.Desfecho.Should().Be(DesfechoEnvio.Enviado);
        var mensagem = (await mailpit.AguardarAsync(1)).Single();
        var anexo = mensagem.Anexos.Should().ContainSingle().Subject;
        anexo.NomeDoArquivo.Should().Be("relatorio-diario.pdf");
        anexo.TipoDeConteudo.Should().Be("application/pdf");
        (await mailpit.BaixarAnexoAsync(mensagem.Id, anexo.IdDaParte)).Should().Equal(conteudo, "o anexo chega byte a byte");
    }

    [SkippableFact]
    public async Task CanalDoOutboxEntregaComCategoriaCerta()
    {
        await PrepararAsync();
        var canal = new SmtpEmailCanal(CriarServico(), NullLogger<SmtpEmailCanal>.Instance);

        MensagemPronta Mensagem(string assunto, CategoriaConteudoNotificacao categoria) =>
            new(Guid.NewGuid(), Guid.NewGuid(), Destinatario, assunto, HtmlComAcentos, CanalNotificacao.Email, categoria);

        var seguranca = await canal.EnviarAsync(Mensagem("Segurança", CategoriaConteudoNotificacao.Seguranca));
        var operacional = await canal.EnviarAsync(Mensagem("Operacional", CategoriaConteudoNotificacao.Operacional));

        seguranca.Desfecho.Should().Be(DesfechoEnvio.Enviado);
        seguranca.ProviderUsado.Should().Be("smtp");
        operacional.Desfecho.Should().Be(DesfechoEnvio.Enviado);
        var chegadas = await mailpit.AguardarAsync(2);
        var deSeguranca = chegadas.Single(m => m.Assunto == "Segurança");
        var deAvisos = chegadas.Single(m => m.Assunto == "Operacional");
        deSeguranca.DeEndereco.Should().Be("seguranca@easystok.online");
        deAvisos.DeEndereco.Should().Be("avisos@easystok.online");
        foreach (var mensagem in chegadas)
        {
            mensagem.Html.TrimEnd(SemQuebraFinal).Should().Be(HtmlComAcentos, "as duas chegam em HTML integro");
            mensagem.Cabecalho("Auto-Submitted").Should().Be("auto-generated");
        }
    }
}
