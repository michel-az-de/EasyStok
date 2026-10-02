using System.Diagnostics;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Infra.Async.Email;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MimeKit;

namespace EasyStock.Infra.Async.UnitTests.Email;

/// <summary>
/// N3 (#1351): o <c>SmtpEmailService</c> sobre MailKit contra um SMTP falso em loopback. Uma conexão por envio, teto de
/// tempo, cancelamento, concorrência e classificação por protocolo, sem retentativa dentro do serviço.
///
/// O arquivo da spec é <c>SmtpEmailServiceTests.cs</c> ("estende o arquivo da N2"). Enquanto a N2 não mergeou, a N3 usa um
/// arquivo e uma classe próprios para não dar add/add com ela; depois do merge das duas, os testes se juntam num só.
/// </summary>
public class SmtpEmailServiceMailKitTests
{
    private const string Senha = "senha-secreta-123";
    private const string Destinatario = "maria.souza@example.com";

    // O CRLF final e do protocolo (termina a ultima linha antes do ponto), nao do conteudo da mensagem.
    private static readonly char[] SemQuebraFinal = ['\r', '\n'];

    private static SmtpConfiguracao Configuracao(
        ServidorSmtpDeTeste servidor,
        SmtpModo modo = SmtpModo.Nenhum,
        int timeoutSegundos = 5,
        Action<SmtpOpcoes>? ajustar = null)
    {
        var opcoes = new SmtpOpcoes
        {
            Host = "127.0.0.1",
            Port = servidor.Porta.ToString(),
            Modo = modo.ToString(),
            TimeoutSegundos = timeoutSegundos.ToString(),
            FromEmail = "avisos@easystok.online",
            FromName = "EasyStok Avisos",
        };
        ajustar?.Invoke(opcoes);
        return opcoes.Resolver("Development");
    }

    private static SmtpEmailService Servico(
        SmtpConfiguracao configuracao,
        ILogger<SmtpEmailService>? logger = null,
        bool aceitarQualquerCertificado = false) =>
        new(configuracao, logger ?? NullLogger<SmtpEmailService>.Instance,
            aceitarQualquerCertificado ? (_, _, _, _) => true : null);

    private static MensagemEmail Mensagem(
        string destinatario = Destinatario,
        RemetenteEmail remetente = RemetenteEmail.Avisos,
        bool html = true,
        Guid? outboxId = null,
        IReadOnlyList<EmailAttachment>? anexos = null) =>
        new(destinatario, "Confirmação de cadastro", html ? "<p>Olá, <b>Maria</b>! Ação concluída.</p>" : "Olá, Maria! Ação concluída.",
            html, anexos, remetente, outboxId);

    private static MimeMessage Ler(ServidorSmtpDeTeste.MensagemRecebida recebida)
    {
        using var fluxo = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(recebida.Bruto));
        return MimeMessage.Load(fluxo);
    }

    private static async Task AguardarAsync(Func<bool> condicao, TimeSpan limite)
    {
        var relogio = Stopwatch.StartNew();
        while (!condicao() && relogio.Elapsed < limite)
            await Task.Delay(25);
    }

    [Fact]
    public async Task EnvioFelizEntregaComRemetenteDeAvisosAutoSubmittedEMessageIdNoDominio()
    {
        await using var servidor = new ServidorSmtpDeTeste().Iniciar();
        var servico = Servico(Configuracao(servidor));

        var resultado = await servico.EnviarAsync(Mensagem(outboxId: Guid.NewGuid()));

        resultado.Desfecho.Should().Be(DesfechoEnvio.Enviado);
        resultado.Sucesso.Should().BeTrue();
        resultado.ProviderUsado.Should().Be("smtp");
        servidor.Mensagens.Should().ContainSingle();

        var recebida = servidor.Mensagens.Single();
        recebida.Remetente.Should().Be("avisos@easystok.online");
        recebida.Destinatarios.Should().Equal(Destinatario);

        var mime = Ler(recebida);
        mime.From.Mailboxes.Single().Address.Should().Be("avisos@easystok.online");
        mime.From.Mailboxes.Single().Name.Should().Be("EasyStok Avisos");
        mime.Headers["Auto-Submitted"].Should().Be("auto-generated");
        mime.MessageId.Should().EndWith("@easystok.online");
        mime.Subject.Should().Be("Confirmação de cadastro");
        mime.HtmlBody.Should().NotBeNull();
        mime.HtmlBody!.TrimEnd(SemQuebraFinal).Should().Be("<p>Olá, <b>Maria</b>! Ação concluída.</p>");
    }

    [Fact]
    public async Task MensagemEmTextoPuroChegaSemHtml()
    {
        await using var servidor = new ServidorSmtpDeTeste().Iniciar();
        var servico = Servico(Configuracao(servidor));

        var resultado = await servico.EnviarAsync(Mensagem(html: false));

        resultado.Desfecho.Should().Be(DesfechoEnvio.Enviado);
        var mime = Ler(servidor.Mensagens.Single());
        mime.HtmlBody.Should().BeNull();
        mime.TextBody.Should().NotBeNull();
        mime.TextBody!.TrimEnd(SemQuebraFinal).Should().Be("Olá, Maria! Ação concluída.");
    }

    [Fact]
    public async Task AnexoViajaComNomeTipoEConteudo()
    {
        await using var servidor = new ServidorSmtpDeTeste().Iniciar();
        var servico = Servico(Configuracao(servidor));
        var conteudo = Enumerable.Range(0, 5000).Select(i => (byte)(i % 251)).ToArray();

        var resultado = await servico.EnviarAsync(Mensagem(anexos: [new EmailAttachment("relatorio.pdf", conteudo, "application/pdf")]));

        resultado.Desfecho.Should().Be(DesfechoEnvio.Enviado);
        var anexo = Ler(servidor.Mensagens.Single()).Attachments.OfType<MimePart>().Single();
        anexo.FileName.Should().Be("relatorio.pdf");
        anexo.ContentType.MimeType.Should().Be("application/pdf");
        anexo.Content.Should().NotBeNull();
        using var memoria = new MemoryStream();
        await anexo.Content!.DecodeToAsync(memoria);
        memoria.ToArray().Should().Equal(conteudo);
    }

    [Fact]
    public async Task NomeDeAnexoComAcentoChegaDecodificado()
    {
        // RFC 2231: o MimeKit codifica o filename com acento e o parser do destinatario o devolve igual.
        await using var servidor = new ServidorSmtpDeTeste().Iniciar();
        var servico = Servico(Configuracao(servidor));

        var resultado = await servico.EnviarAsync(Mensagem(anexos: [new EmailAttachment("relatório-diário.pdf", [1, 2, 3], "application/pdf")]));

        resultado.Desfecho.Should().Be(DesfechoEnvio.Enviado);
        Ler(servidor.Mensagens.Single()).Attachments.OfType<MimePart>().Single().FileName.Should().Be("relatório-diário.pdf");
    }

    [Fact]
    public async Task CadaCaixaAutenticaComASuaCredencialESaiComOSeuRemetente()
    {
        await using var servidor = new ServidorSmtpDeTeste
        {
            Tls = ServidorSmtpDeTeste.ModoTls.StartTls,
            AnunciaAuth = true,
            ExigeAuth = true,
        }.Iniciar();
        var configuracao = Configuracao(servidor, SmtpModo.StartTls, ajustar: o =>
        {
            o.Username = "avisos@easystok.online";
            o.Password = "senha-avisos";
            o.Seguranca = new SmtpRemetenteOpcoes
            {
                Username = "seguranca@easystok.online",
                Password = "senha-seguranca",
                FromEmail = "seguranca@easystok.online",
                FromName = "EasyStok Segurança",
            };
        });
        var servico = Servico(configuracao, aceitarQualquerCertificado: true);

        var seguranca = await servico.EnviarAsync(Mensagem(remetente: RemetenteEmail.Seguranca));
        var avisos = await servico.EnviarAsync(Mensagem(remetente: RemetenteEmail.Avisos));

        seguranca.Desfecho.Should().Be(DesfechoEnvio.Enviado);
        avisos.Desfecho.Should().Be(DesfechoEnvio.Enviado);
        var deSeguranca = servidor.Mensagens.Single(m => m.Remetente == "seguranca@easystok.online");
        deSeguranca.UsuarioAutenticado.Should().Be("seguranca@easystok.online");
        Ler(deSeguranca).From.Mailboxes.Single().Name.Should().Be("EasyStok Segurança");
        var deAvisos = servidor.Mensagens.Single(m => m.Remetente == "avisos@easystok.online");
        deAvisos.UsuarioAutenticado.Should().Be("avisos@easystok.online");
    }

    [Fact]
    public async Task ServidorMudoEstouraNoTetoConfigurado()
    {
        await using var servidor = new ServidorSmtpDeTeste { Mudo = true }.Iniciar();
        var servico = Servico(Configuracao(servidor, timeoutSegundos: 2));
        var relogio = Stopwatch.StartNew();

        var resultado = await servico.EnviarAsync(Mensagem());

        relogio.Stop();
        resultado.Desfecho.Should().Be(DesfechoEnvio.FalhaTransitoria);
        resultado.ErroDetalhado.Should().Contain("tempo limite");
        relogio.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(4), "o teto de 2 s vale, não o de 100 s do SmtpClient antigo");
        await AguardarAsync(() => servidor.ConexoesAbertas == 0, TimeSpan.FromSeconds(3));
        servidor.ConexoesAbertas.Should().Be(0, "estourar o teto não deixa conexão aberta");
    }

    [Fact]
    public async Task CancelarOTokenInterrompeOEnvio()
    {
        await using var servidor = new ServidorSmtpDeTeste { TravarAposData = true }.Iniciar();
        var servico = Servico(Configuracao(servidor, timeoutSegundos: 30));
        using var cancelamento = new CancellationTokenSource();

        var envio = servico.EnviarAsync(Mensagem(), cancelamento.Token);
        await servidor.DataIniciado.WaitAsync(TimeSpan.FromSeconds(10));
        await cancelamento.CancelAsync();

        var act = async () => await envio;
        await act.Should().ThrowAsync<OperationCanceledException>();
        await AguardarAsync(() => servidor.ConexoesAbertas == 0, TimeSpan.FromSeconds(3));
        servidor.ConexoesAbertas.Should().Be(0, "cancelar no meio do envio não pode deixar conexão aberta");
        servidor.Mensagens.Should().BeEmpty();
    }

    [Fact]
    public async Task TokenJaCanceladoNaoAbreConexao()
    {
        await using var servidor = new ServidorSmtpDeTeste().Iniciar();
        var servico = Servico(Configuracao(servidor));
        using var cancelamento = new CancellationTokenSource();
        await cancelamento.CancelAsync();

        var act = async () => await servico.EnviarAsync(Mensagem(), cancelamento.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        servidor.ConexoesAceitas.Should().Be(0);
    }

    [Fact]
    public async Task DezEnviosConcorrentesChegamTodos()
    {
        await using var servidor = new ServidorSmtpDeTeste().Iniciar();
        var servico = Servico(Configuracao(servidor));

        var resultados = await Task.WhenAll(Enumerable.Range(1, 10)
            .Select(i => servico.EnviarAsync(Mensagem($"destinatario{i}@example.com"))));

        resultados.Should().OnlyContain(r => r.Desfecho == DesfechoEnvio.Enviado);
        servidor.Mensagens.Should().HaveCount(10);
        servidor.Mensagens.SelectMany(m => m.Destinatarios).Distinct().Should().HaveCount(10);
        servidor.ConexoesAceitas.Should().Be(10, "uma conexão por envio, sem estado compartilhado");
    }

    [Fact]
    public async Task Rcpt550GeraFalhaPermanenteComUmaUnicaConexao()
    {
        await using var servidor = new ServidorSmtpDeTeste { RespostaRcpt = "550 5.1.1 Usuario desconhecido" }.Iniciar();
        var servico = Servico(Configuracao(servidor));

        var resultado = await servico.EnviarAsync(Mensagem());

        resultado.Desfecho.Should().Be(DesfechoEnvio.FalhaPermanente);
        resultado.FalhaPermanente.Should().BeTrue();
        resultado.Sucesso.Should().BeFalse();
        resultado.ErroDetalhado.Should().Contain("550");
        servidor.ConexoesAceitas.Should().Be(1, "uma tentativa por chamada, sem retentativa no serviço");
        servidor.RcptRecebidos.Should().Be(1);
        servidor.Mensagens.Should().BeEmpty();
    }

    [Fact]
    public async Task Resposta421GeraFalhaTransitoria()
    {
        await using var servidor = new ServidorSmtpDeTeste { Saudacao = "421 4.3.2 Servico indisponivel" }.Iniciar();
        var servico = Servico(Configuracao(servidor));

        var resultado = await servico.EnviarAsync(Mensagem());

        resultado.Desfecho.Should().Be(DesfechoEnvio.FalhaTransitoria);
        resultado.FalhaPermanente.Should().BeFalse();
        resultado.ErroDetalhado.Should().Contain("421");
        servidor.ConexoesAceitas.Should().Be(1, "o 421 não é retentado dentro do serviço");
    }

    [Fact]
    public async Task EnderecoInvalidoEFalhaPermanenteSemAbrirConexao()
    {
        await using var servidor = new ServidorSmtpDeTeste().Iniciar();
        var servico = Servico(Configuracao(servidor));

        var resultado = await servico.EnviarAsync(Mensagem("isto-nao-e-um-email"));

        resultado.Desfecho.Should().Be(DesfechoEnvio.FalhaPermanente);
        servidor.ConexoesAceitas.Should().Be(0);
    }

    [Fact]
    public async Task AutenticacaoRecusadaViraFalhaPermanenteQueNomeiaAsChavesENuncaASenha()
    {
        await using var servidor = new ServidorSmtpDeTeste
        {
            Tls = ServidorSmtpDeTeste.ModoTls.StartTls,
            AnunciaAuth = true,
            RespostaAuth = "535 5.7.8 Credenciais invalidas",
        }.Iniciar();
        var logs = new ColetorDeLogs<SmtpEmailService>();
        var configuracao = Configuracao(servidor, SmtpModo.StartTls, ajustar: o =>
        {
            o.Username = "avisos@easystok.online";
            o.Password = Senha;
        });
        var servico = Servico(configuracao, logs, aceitarQualquerCertificado: true);

        var resultado = await servico.EnviarAsync(Mensagem(outboxId: Guid.NewGuid()));

        resultado.Desfecho.Should().Be(DesfechoEnvio.FalhaPermanente);
        resultado.ErroDetalhado.Should().Contain("Smtp:Username").And.Contain("Smtp:Password");
        resultado.ErroDetalhado.Should().NotContain(Senha);
        logs.Linhas.Should().Contain(l => l.Nivel == LogLevel.Error && l.Texto.Contains("Smtp:Username"));
        logs.Linhas.Should().NotContain(l => l.Texto.Contains(Senha));
    }

    [Fact]
    public async Task TlsImplicitoEntregaNoModoSslImplicito()
    {
        await using var servidor = new ServidorSmtpDeTeste { Tls = ServidorSmtpDeTeste.ModoTls.Implicito }.Iniciar();
        var servico = Servico(Configuracao(servidor, SmtpModo.SslImplicito), aceitarQualquerCertificado: true);

        var resultado = await servico.EnviarAsync(Mensagem());

        resultado.Desfecho.Should().Be(DesfechoEnvio.Enviado);
        servidor.Mensagens.Should().ContainSingle();
    }

    [Fact]
    public async Task StartTlsEntregaQuandoOServidorOferece()
    {
        await using var servidor = new ServidorSmtpDeTeste { Tls = ServidorSmtpDeTeste.ModoTls.StartTls }.Iniciar();
        var servico = Servico(Configuracao(servidor, SmtpModo.StartTls), aceitarQualquerCertificado: true);

        var resultado = await servico.EnviarAsync(Mensagem());

        resultado.Desfecho.Should().Be(DesfechoEnvio.Enviado);
        servidor.Mensagens.Should().ContainSingle();
    }

    [Fact]
    public async Task StartTlsObrigatorioNaoRebaixaParaTextoPuro()
    {
        // Servidor que não oferece STARTTLS (ou um atacante que o removeu do EHLO): nada pode sair em claro.
        await using var servidor = new ServidorSmtpDeTeste { Tls = ServidorSmtpDeTeste.ModoTls.Nenhum }.Iniciar();
        var servico = Servico(Configuracao(servidor, SmtpModo.StartTls));

        var resultado = await servico.EnviarAsync(Mensagem());

        resultado.Desfecho.Should().Be(DesfechoEnvio.FalhaTransitoria);
        servidor.Mensagens.Should().BeEmpty("a mensagem não pode ter saído sem TLS");
        servidor.RcptRecebidos.Should().Be(0);
    }

    [Fact]
    public async Task CertificadoNaoConfiavelERecusadoPorPadrao()
    {
        await using var servidor = new ServidorSmtpDeTeste { Tls = ServidorSmtpDeTeste.ModoTls.StartTls }.Iniciar();
        var servico = Servico(Configuracao(servidor, SmtpModo.StartTls));

        var resultado = await servico.EnviarAsync(Mensagem());

        resultado.Desfecho.Should().Be(DesfechoEnvio.FalhaTransitoria);
        servidor.Mensagens.Should().BeEmpty("o certificado autoassinado não é confiável e o serviço não afrouxa a validação sozinho");
    }

    [Fact]
    public async Task LogNaoTemEnderecoNemSenhaEQuemLevaAoEnvioEOOutboxId()
    {
        var outboxOk = Guid.NewGuid();
        var outboxFalha = Guid.NewGuid();
        await using var servidorOk = new ServidorSmtpDeTeste().Iniciar();
        await using var servidorFalha = new ServidorSmtpDeTeste { RespostaRcpt = $"550 5.1.1 <{Destinatario}> Usuario desconhecido" }.Iniciar();
        var logs = new ColetorDeLogs<SmtpEmailService>();

        await Servico(Configuracao(servidorOk, ajustar: o => o.Password = Senha), logs)
            .EnviarAsync(Mensagem(remetente: RemetenteEmail.Seguranca, outboxId: outboxOk));
        var falha = await Servico(Configuracao(servidorFalha, ajustar: o => o.Password = Senha), logs)
            .EnviarAsync(Mensagem(outboxId: outboxFalha));

        logs.Linhas.Should().NotBeEmpty();
        logs.Linhas.Should().NotContain(l => l.Texto.Contains("maria.souza") || l.Texto.Contains("example.com"),
            "endereço de e-mail é dado pessoal (LGPD)");
        logs.Linhas.Should().NotContain(l => l.Texto.Contains(Senha));
        logs.Linhas.Should().OnlyContain(l => l.Texto.Contains(outboxOk.ToString()) || l.Texto.Contains(outboxFalha.ToString()),
            "o OutboxId é o rastro do envio");
        logs.Linhas.Should().Contain(l => l.Texto.Contains("Seguranca"), "a categoria entra no log");
        falha.ErroDetalhado.Should().NotContain("maria.souza", "o detalhe vai para o banco e não leva o endereço");
    }

    [Fact]
    public async Task MetodosAntigosEntregamComRemetenteDeAvisosELancamQuandoFalha()
    {
        await using var servidorOk = new ServidorSmtpDeTeste().Iniciar();
        await using var servidorRecusa = new ServidorSmtpDeTeste { RespostaRcpt = "550 5.1.1 Usuario desconhecido" }.Iniciar();
        IEmailService ok = Servico(Configuracao(servidorOk));
        IEmailService recusa = Servico(Configuracao(servidorRecusa));

        await ok.SendAsync(Destinatario, "Relatório", "<p>ok</p>", isHtml: true);
        await ok.SendAsync(Destinatario, "Com anexo", "texto", [new EmailAttachment("a.txt", [1, 2, 3], "text/plain")]);
        await ok.SendAsync(new[] { "a@example.com", "b@example.com" }, "Dois destinatários", "texto");

        // A sobrecarga de vários destinatários manda uma mensagem por pessoa: ninguém vê o endereço do outro.
        servidorOk.Mensagens.Should().HaveCount(4);
        servidorOk.Mensagens.Should().OnlyContain(m => m.Remetente == "avisos@easystok.online");
        servidorOk.Mensagens.TakeLast(2).SelectMany(m => m.Destinatarios)
            .Should().BeEquivalentTo("a@example.com", "b@example.com");

        var act = async () => await recusa.SendAsync(Destinatario, "Relatório", "texto");
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*550*");
    }
}
