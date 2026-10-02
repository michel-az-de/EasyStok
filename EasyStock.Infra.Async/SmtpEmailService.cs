using System.Diagnostics;
using System.Net.Security;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Infra.Async.Email;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using MimeKit;
using MimeKit.Utils;

namespace EasyStock.Infra.Async;

/// <summary>
/// Servico de e-mail por SMTP sobre MailKit (N3, #1351). Sem estado compartilhado: uma conexao por envio
/// (<c>Connect</c>, <c>Authenticate</c> quando ha usuario, <c>Send</c>, <c>Disconnect</c>), entao o singleton e seguro
/// para a API e para o Dispatcher ao mesmo tempo.
/// <list type="bullet">
///   <item>Uma tentativa por chamada: a retentativa e do outbox, e so dele.</item>
///   <item>Teto de <c>Smtp:TimeoutSegundos</c> por envio, encadeado ao token do chamador. Estourar o teto e falha
///   transitoria; cancelar o token do chamador propaga <see cref="OperationCanceledException"/>.</item>
///   <item>Transporte por modo resolvido: 465 TLS implicito, o resto STARTTLS obrigatorio. Nunca "se o servidor oferecer".</item>
///   <item>Log sem endereco de e-mail e sem senha (LGPD): so OutboxId, categoria, duracao e codigo SMTP. O
///   <c>ProtocolLogger</c> do MailKit nunca liga: ele imprime as credenciais do AUTH.</item>
/// </list>
/// </summary>
public sealed class SmtpEmailService : IEmailService
{
    private static readonly TimeSpan TetoDoEncerramento = TimeSpan.FromSeconds(2);

    private readonly SmtpConfiguracao _configuracao;
    private readonly ILogger<SmtpEmailService> _logger;
    private readonly RemoteCertificateValidationCallback? _validadorDeCertificado;

    public SmtpEmailService(SmtpConfiguracao configuracao, ILogger<SmtpEmailService> logger)
        : this(configuracao, logger, null)
    {
    }

    /// <summary>Seam de teste: valida o certificado (o servidor falso e o Mailpit usam certificado proprio).</summary>
    internal SmtpEmailService(
        SmtpConfiguracao configuracao,
        ILogger<SmtpEmailService> logger,
        RemoteCertificateValidationCallback? validadorDeCertificado)
    {
        _configuracao = configuracao;
        _logger = logger;
        _validadorDeCertificado = validadorDeCertificado;
    }

    public async Task<ResultadoEnvio> EnviarAsync(MensagemEmail mensagem, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(mensagem);
        ct.ThrowIfCancellationRequested();

        var remetente = mensagem.Remetente == RemetenteEmail.Seguranca ? _configuracao.Seguranca : _configuracao.Avisos;
        var cronometro = Stopwatch.StartNew();

        using var teto = CancellationTokenSource.CreateLinkedTokenSource(ct);
        teto.CancelAfter(_configuracao.Timeout);

        try
        {
            using var mime = ConstruirMime(mensagem, remetente);
            using var cliente = NovoCliente();

            await cliente.ConnectAsync(_configuracao.Host, _configuracao.Porta, OpcaoDeSeguranca(), teto.Token);

            if (!string.IsNullOrEmpty(remetente.Username))
                await cliente.AuthenticateAsync(remetente.Username, remetente.Password ?? string.Empty, teto.Token);

            await cliente.SendAsync(mime, teto.Token);

            // A mensagem ja foi aceita: um QUIT que falha nao pode virar falha de envio (duplicaria na retentativa).
            await EncerrarSemFalharAsync(cliente);

            cronometro.Stop();
            _logger.LogInformation(
                "E-mail enviado por SMTP outbox={OutboxId} categoria={Categoria} em {Ms}ms",
                mensagem.OutboxId, mensagem.Remetente, cronometro.ElapsedMilliseconds);

            return new ResultadoEnvio(Sucesso: true, ProviderUsado: "smtp", DuracaoMs: cronometro.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            // Cancelamento do chamador sempre propaga, venha como OperationCanceledException ou como o
            // IOException/SocketException que o socket fechado pelo cancelamento produz.
            ct.ThrowIfCancellationRequested();

            // Endereco ou anexo malformado estoura aqui, antes de abrir conexao, e vira falha permanente.
            var falha = ClassificadorFalhaSmtp.Classificar(ex, teto.IsCancellationRequested, remetente.ChaveBase);
            return Falha(falha, mensagem, cronometro);
        }
    }

    public Task SendAsync(string to, string subject, string body, bool isHtml = false) =>
        SendAsync(new[] { to }, subject, body, Enumerable.Empty<EmailAttachment>(), isHtml);

    public Task SendAsync(string to, string subject, string body, IEnumerable<EmailAttachment> attachments, bool isHtml = false) =>
        SendAsync(new[] { to }, subject, body, attachments, isHtml);

    public Task SendAsync(IEnumerable<string> to, string subject, string body, bool isHtml = false) =>
        SendAsync(to, subject, body, Enumerable.Empty<EmailAttachment>(), isHtml);

    /// <summary>
    /// Metodo antigo: uma mensagem por destinatario (ninguem ve o endereco do outro), remetente de avisos, e lanca
    /// quando algum envio falha, como antes. Quem precisa do desfecho tipado usa <see cref="EnviarAsync"/>.
    /// </summary>
    public async Task SendAsync(IEnumerable<string> to, string subject, string body, IEnumerable<EmailAttachment> attachments, bool isHtml = false)
    {
        var anexos = attachments.ToList();
        var falhas = new List<string>();
        var total = 0;
        foreach (var destinatario in to)
        {
            total++;
            var resultado = await EnviarAsync(new MensagemEmail(destinatario, subject, body, isHtml, anexos));
            if (resultado.Desfecho is DesfechoEnvio.FalhaTransitoria or DesfechoEnvio.FalhaPermanente)
                falhas.Add(resultado.ErroDetalhado ?? resultado.Desfecho.ToString());
        }

        if (falhas.Count > 0)
            throw new InvalidOperationException(
                $"Falha no envio de e-mail ({falhas.Count} de {total} destinatario(s)): {falhas[0]}");
    }

    public Task SendTemplateAsync(string to, string subject, string templateName, object model, bool isHtml = true)
    {
        // Implementacao basica - em producao usar template engine como Razor ou Handlebars
        var body = $"Template: {templateName}\n\nModel: {System.Text.Json.JsonSerializer.Serialize(model)}";
        return SendAsync(to, subject, body, isHtml);
    }

    private SmtpClient NovoCliente()
    {
        var cliente = new SmtpClient
        {
            // Teto de rede do proprio MailKit, alem do CancelAfter do token: o que vier primeiro corta o envio.
            Timeout = (int)Math.Min(int.MaxValue, _configuracao.Timeout.TotalMilliseconds),
        };

        if (_validadorDeCertificado is not null)
            cliente.ServerCertificateValidationCallback = _validadorDeCertificado;

        return cliente;
    }

    private SecureSocketOptions OpcaoDeSeguranca() => _configuracao.Modo switch
    {
        SmtpModo.SslImplicito => SecureSocketOptions.SslOnConnect,
        SmtpModo.StartTls => SecureSocketOptions.StartTls,
        SmtpModo.Nenhum => SecureSocketOptions.None,

        // Nunca Auto nem StartTlsWhenAvailable: aceitam rebaixar para texto puro.
        _ => throw new InvalidOperationException(
            $"Smtp:Modo '{_configuracao.Modo}' não resolvido: a configuração deveria ter vindo de SmtpOpcoes.Resolver."),
    };

    private static async Task EncerrarSemFalharAsync(SmtpClient cliente)
    {
        try
        {
            using var curto = new CancellationTokenSource(TetoDoEncerramento);
            await cliente.DisconnectAsync(true, curto.Token);
        }
        catch (Exception)
        {
            // Dispose do cliente fecha o socket de qualquer jeito.
        }
    }

    private static MimeMessage ConstruirMime(MensagemEmail mensagem, SmtpRemetente remetente)
    {
        if (!MailboxAddress.TryParse(mensagem.Destinatario, out var para) || !para.Address.Contains('@'))
            throw new FormatException("Destinatario invalido.");

        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(remetente.Nome, remetente.Email));
        mime.To.Add(para);
        mime.Subject = mensagem.Assunto ?? string.Empty;

        // Mensagem de sistema: MTAs e auto-respondedores nao respondem a ela (evita laco de resposta automatica).
        mime.Headers.Add("Auto-Submitted", "auto-generated");
        mime.MessageId = MimeUtils.GenerateMessageId(remetente.Email[(remetente.Email.LastIndexOf('@') + 1)..]);

        var corpo = new BodyBuilder();
        if (mensagem.Html)
            corpo.HtmlBody = mensagem.Corpo;
        else
            corpo.TextBody = mensagem.Corpo;

        foreach (var anexo in mensagem.Anexos ?? [])
            corpo.Attachments.Add(anexo.FileName, anexo.Content, ContentType.Parse(anexo.ContentType));

        mime.Body = corpo.ToMessageBody();
        return mime;
    }

    private ResultadoEnvio Falha(FalhaSmtp falha, MensagemEmail mensagem, Stopwatch cronometro)
    {
        cronometro.Stop();

        if (falha.ErroDeConfiguracao)
        {
            _logger.LogError(
                "Configuração de e-mail com problema outbox={OutboxId} categoria={Categoria} smtp={CodigoSmtp}: {Detalhe}",
                mensagem.OutboxId, mensagem.Remetente, falha.CodigoSmtp, falha.Detalhe);
        }
        else
        {
            _logger.LogWarning(
                "Falha no envio SMTP outbox={OutboxId} categoria={Categoria} desfecho={Desfecho} smtp={CodigoSmtp} em {Ms}ms",
                mensagem.OutboxId, mensagem.Remetente, falha.Desfecho, falha.CodigoSmtp, cronometro.ElapsedMilliseconds);
        }

        return new ResultadoEnvio(
            Sucesso: false,
            ProviderUsado: "smtp",
            ErroDetalhado: falha.Detalhe,
            DuracaoMs: cronometro.ElapsedMilliseconds,
            FalhaPermanente: falha.Desfecho == DesfechoEnvio.FalhaPermanente);
    }
}
