using System.Text;
using EasyStock.Infra.Async.Email.Atendimento;
using FluentAssertions;
using MimeKit;

namespace EasyStock.Infra.Async.UnitTests.Email.Atendimento;

/// <summary>#1432: o MIME do e-mail recebido para o formato do atendimento, com e-mails montados como os clientes mandam.</summary>
public class LeitorMimeEmailTests
{
    private static readonly DateTime Agora = new(2026, 10, 7, 13, 0, 0, DateTimeKind.Utc);

    private static MimeMessage Ler(string bruto) =>
        MimeMessage.Load(new MemoryStream(Encoding.UTF8.GetBytes(bruto.ReplaceLineEndings("\r\n"))));

    [Fact]
    public void Multipart_PrefereOTextoPuro_EGuardaMessageIdAssuntoEData()
    {
        using var mime = Ler("""
            From: Maria Souza <maria@exemplo.com>
            To: contato@casadababa.com
            Subject: =?UTF-8?Q?Encomenda_de_bolo_para_s=C3=A1bado?=
            Date: Wed, 07 Oct 2026 09:58:00 -0300
            Message-ID: <abc123@mail.gmail.com>
            MIME-Version: 1.0
            Content-Type: multipart/alternative; boundary="b1"

            --b1
            Content-Type: text/plain; charset=utf-8

            Quero 2 lasanhas.
            --b1
            Content-Type: text/html; charset=utf-8

            <p>Quero <b>2</b> lasanhas.</p>
            --b1--
            """);

        var email = LeitorMimeEmail.Ler("42", mime, Agora);

        email.IdNaCaixa.Should().Be("42");
        email.MessageId.Should().Be("abc123@mail.gmail.com");
        email.DeEndereco.Should().Be("maria@exemplo.com");
        email.DeNome.Should().Be("Maria Souza");
        email.Assunto.Should().Be("Encomenda de bolo para sábado");
        email.Texto.Trim().Should().Be("Quero 2 lasanhas.");
        email.RecebidoEm.Should().Be(new DateTime(2026, 10, 7, 12, 58, 0, DateTimeKind.Utc));
        email.AutoGerado.Should().BeFalse();
        email.Anexos.Should().BeEmpty();
    }

    [Fact]
    public void SoHtml_ViraTextoSemScriptNemCitacao()
    {
        using var mime = Ler("""
            From: maria@exemplo.com
            Subject: Oi
            Content-Type: text/html; charset=utf-8

            <html><head><style>p{color:red}</style></head><body>
            <p>Bom dia &amp; obrigada!</p><div>Linha 2<br>Linha 3</div>
            <script>alert('x')</script><img src="https://rastreio.example/p.gif">
            <ul><li>Lasanha</li><li>Ravióli</li></ul>
            <blockquote>Histórico citado</blockquote>
            </body></html>
            """);

        var texto = LeitorMimeEmail.Ler("1", mime, Agora).Texto;

        texto.Should().Be("Bom dia & obrigada!\nLinha 2\nLinha 3\n- Lasanha\n- Ravióli");
        texto.Should().NotContain("alert").And.NotContain("color").And.NotContain("Histórico").And.NotContain("rastreio");
    }

    [Fact]
    public void ReplyTo_GanhaDoFrom_FormularioDoSite()
    {
        using var mime = Ler("""
            From: Site <noreply@casadababa.com>
            Reply-To: Joana <joana@exemplo.com>
            Subject: Fale conosco
            Content-Type: text/plain

            Mensagem do formulário
            """);

        var email = LeitorMimeEmail.Ler("1", mime, Agora);

        email.DeEndereco.Should().Be("joana@exemplo.com");
        email.DeNome.Should().Be("Joana");
        email.MessageId.Should().BeNull();
        email.RecebidoEm.Should().Be(Agora, "sem Date vale a hora da leitura");
    }

    [Theory]
    [InlineData("Auto-Submitted: auto-replied", true)]
    [InlineData("Auto-Submitted: no", false)]
    [InlineData("X-Autoreply: yes", true)]
    [InlineData("Precedence: bulk", true)]
    [InlineData("Precedence: list", false)]
    public void RespostaAutomatica_PelosCabecalhos(string cabecalho, bool automatica)
    {
        using var mime = Ler($"""
            From: maria@exemplo.com
            {cabecalho}
            Subject: Ausente
            Content-Type: text/plain

            Estou de férias.
            """);

        LeitorMimeEmail.Ler("1", mime, Agora).AutoGerado.Should().Be(automatica);
    }

    [Fact]
    public void EmailGigante_EntraSoOEnvelopeComAviso()
    {
        var envelope = new MailKit.Envelope { Subject = "Fotos da festa", MessageId = "big@x" };
        envelope.From.Add(new MailboxAddress("Maria", "maria@exemplo.com"));

        var email = LeitorMimeEmail.SoEnvelope("9", envelope, 52L * 1024 * 1024, Agora);

        email.DeEndereco.Should().Be("maria@exemplo.com");
        email.MessageId.Should().Be("big@x");
        email.Assunto.Should().Be("Fotos da festa");
        email.Texto.Should().Contain("52 MB").And.Contain("abra na caixa da loja");
        email.RecebidoEm.Should().Be(Agora);
        email.Anexos.Should().BeEmpty();
    }

    [Fact]
    public void Anexo_EntraComNomeETipo_EImagemEmLinhaDaAssinaturaNao()
    {
        using var mime = Ler("""
            From: maria@exemplo.com
            Subject: Orçamento
            Content-Type: multipart/mixed; boundary="m"

            --m
            Content-Type: multipart/related; boundary="r"

            --r
            Content-Type: text/html; charset=utf-8

            <p>Segue o orçamento.</p><img src="cid:logo">
            --r
            Content-Type: image/png
            Content-Disposition: inline; filename="logo.png"
            Content-ID: <logo>
            Content-Transfer-Encoding: base64

            iVBORw0KGgo=
            --r--
            --m
            Content-Type: application/pdf; name="orcamento.pdf"
            Content-Disposition: attachment; filename="orcamento.pdf"
            Content-Transfer-Encoding: base64

            JVBERi0xLjQK
            --m--
            """);

        var email = LeitorMimeEmail.Ler("1", mime, Agora);

        email.Texto.Should().Be("Segue o orçamento.");
        var anexo = email.Anexos.Should().ContainSingle().Subject;
        anexo.NomeArquivo.Should().Be("orcamento.pdf");
        anexo.Mime.Should().Be("application/pdf");
        Encoding.ASCII.GetString(anexo.Conteudo!).Should().StartWith("%PDF-1.4");
    }
}
