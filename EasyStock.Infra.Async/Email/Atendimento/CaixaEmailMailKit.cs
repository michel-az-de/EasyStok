using System.Net.Security;
using System.Net.Sockets;
using EasyStock.Application.Ports.Output.Atendimento.Email;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Net.Smtp;
using MailKit.Search;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using MimeKit;
using MimeKit.Utils;

namespace EasyStock.Infra.Async.Email.Atendimento;

/// <summary>
/// Caixa de suporte da loja sobre MailKit (#1432). TLS pela porta: 993 e 465 implícito, qualquer outra STARTTLS
/// obrigatório (nunca texto puro nem "se o servidor oferecer"). Uma conexão por operação, com teto de tempo. O
/// <c>ProtocolLogger</c> do MailKit nunca liga (imprime o AUTH) e o log não leva endereço nem senha.
/// </summary>
public sealed class CaixaEmailMailKit : ICaixaEmailCliente
{
    private static readonly TimeSpan Teto = TimeSpan.FromSeconds(30);

    private readonly ILogger<CaixaEmailMailKit> _logger;
    private readonly RemoteCertificateValidationCallback? _validadorDeCertificado;
    private readonly TimeProvider _relogio;

    public CaixaEmailMailKit(ILogger<CaixaEmailMailKit> logger, TimeProvider relogio)
        : this(logger, relogio, null)
    {
    }

    /// <summary>Seam de teste: o servidor falso usa certificado próprio.</summary>
    internal CaixaEmailMailKit(
        ILogger<CaixaEmailMailKit> logger, TimeProvider relogio, RemoteCertificateValidationCallback? validadorDeCertificado)
    {
        _logger = logger;
        _relogio = relogio;
        _validadorDeCertificado = validadorDeCertificado;
    }

    internal static SecureSocketOptions SegurancaDaPorta(int porta) =>
        porta is 993 or 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls;

    public async Task<ResultadoTesteCaixaEmail> TestarAsync(CaixaEmailAtendimento caixa, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caixa);

        string? erroImap = null;
        try
        {
            await using var _ = await AbrirAsync(caixa, ct);
        }
        catch (FalhaCaixaEmailException ex)
        {
            erroImap = ex.Message;
        }

        string? erroSmtp = null;
        try
        {
            using var teto = Limite(ct);
            using var smtp = NovoSmtp();
            await ExecutarAsync("SMTP", caixa.SmtpHost, caixa.SmtpPorta, teto, ct, async () =>
            {
                await smtp.ConnectAsync(caixa.SmtpHost, caixa.SmtpPorta, SegurancaDaPorta(caixa.SmtpPorta), teto.Token);
                await smtp.AuthenticateAsync(caixa.Usuario, caixa.Senha, teto.Token);
                await EncerrarSemFalharAsync(() => smtp.DisconnectAsync(true, CancellationToken.None));
            });
        }
        catch (FalhaCaixaEmailException ex)
        {
            erroSmtp = ex.Message;
        }

        return new ResultadoTesteCaixaEmail(erroImap is null, erroImap, erroSmtp is null, erroSmtp);
    }

    public async Task<ISessaoCaixaEmail> AbrirAsync(CaixaEmailAtendimento caixa, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caixa);

        var imap = new ImapClient { Timeout = (int)Teto.TotalMilliseconds };
        if (_validadorDeCertificado is not null)
            imap.ServerCertificateValidationCallback = _validadorDeCertificado;
        try
        {
            using var teto = Limite(ct);
            await ExecutarAsync("IMAP", caixa.ImapHost, caixa.ImapPorta, teto, ct, async () =>
            {
                await imap.ConnectAsync(caixa.ImapHost, caixa.ImapPorta, SegurancaDaPorta(caixa.ImapPorta), teto.Token);
                await imap.AuthenticateAsync(caixa.Usuario, caixa.Senha, teto.Token);
                await imap.Inbox.OpenAsync(FolderAccess.ReadWrite, teto.Token);
            });
            return new SessaoImap(imap, _relogio);
        }
        catch
        {
            imap.Dispose();
            throw;
        }
    }

    public async Task<string> EnviarAsync(CaixaEmailAtendimento caixa, EmailSaida email, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caixa);
        ArgumentNullException.ThrowIfNull(email);

        using var mime = Montar(caixa, email);
        using var teto = Limite(ct);
        using var smtp = NovoSmtp();
        await ExecutarAsync("SMTP", caixa.SmtpHost, caixa.SmtpPorta, teto, ct, async () =>
        {
            await smtp.ConnectAsync(caixa.SmtpHost, caixa.SmtpPorta, SegurancaDaPorta(caixa.SmtpPorta), teto.Token);
            await smtp.AuthenticateAsync(caixa.Usuario, caixa.Senha, teto.Token);
            await smtp.SendAsync(mime, teto.Token);
        });
        // A mensagem já foi aceita: QUIT que falha não pode virar falha de envio (duplicaria no reenvio).
        await EncerrarSemFalharAsync(() => smtp.DisconnectAsync(true, CancellationToken.None));
        return mime.MessageId ?? string.Empty; // Montar sempre gera um
    }

    /// <summary>Monta a resposta: From da caixa, In-Reply-To e References para cair no mesmo fio do cliente.</summary>
    internal static MimeMessage Montar(CaixaEmailAtendimento caixa, EmailSaida email)
    {
        if (!MailboxAddress.TryParse(email.Para, out var para) || !para.Address.Contains('@'))
            throw new FalhaCaixaEmailException("destinatário inválido", permanente: true);

        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(caixa.NomeExibicao ?? string.Empty, caixa.Endereco));
        mime.To.Add(para);
        mime.Subject = email.Assunto;
        mime.MessageId = MimeUtils.GenerateMessageId(caixa.Endereco[(caixa.Endereco.LastIndexOf('@') + 1)..]);
        if (!string.IsNullOrWhiteSpace(email.EmRespostaA))
        {
            var id = email.EmRespostaA.Trim().Trim('<', '>');
            mime.InReplyTo = id;
            mime.References.Add(id);
        }

        var corpo = new BodyBuilder();
        if (email.Html) corpo.HtmlBody = email.Corpo;
        else corpo.TextBody = email.Corpo;
        mime.Body = corpo.ToMessageBody();
        return mime;
    }

    private SmtpClient NovoSmtp()
    {
        var smtp = new SmtpClient { Timeout = (int)Teto.TotalMilliseconds };
        if (_validadorDeCertificado is not null)
            smtp.ServerCertificateValidationCallback = _validadorDeCertificado;
        return smtp;
    }

    private static CancellationTokenSource Limite(CancellationToken ct)
    {
        var teto = CancellationTokenSource.CreateLinkedTokenSource(ct);
        teto.CancelAfter(Teto);
        return teto;
    }

    /// <summary>
    /// Traduz a falha do MailKit num motivo curto, sem endereço e sem senha. Login recusado e 5xx do servidor
    /// são permanentes; rede, TLS, 4xx e teto estourado são temporários. Cancelamento de quem chamou propaga.
    /// </summary>
    private async Task ExecutarAsync(
        string protocolo, string host, int porta, CancellationTokenSource teto, CancellationToken ct, Func<Task> operacao)
    {
        try
        {
            await operacao();
        }
        catch (Exception ex) when (ex is not FalhaCaixaEmailException)
        {
            ct.ThrowIfCancellationRequested();
            var (motivo, permanente) = ex switch
            {
                AuthenticationException => ("usuário ou senha recusados", true),
                SslHandshakeException => ($"falha no TLS com {host}:{porta} (porta e segurança não combinam?)", false),
                SmtpCommandException smtp when (int)smtp.StatusCode >= 500 => ($"o servidor recusou ({(int)smtp.StatusCode})", true),
                SmtpCommandException smtp => ($"o servidor recusou por ora ({(int)smtp.StatusCode})", false),
                ImapCommandException => ("o servidor recusou o comando", false),
                OperationCanceledException or TimeoutException when teto.IsCancellationRequested || ex is TimeoutException =>
                    ($"tempo esgotado falando com {host}:{porta}", false),
                SocketException or IOException => ($"não conectou em {host}:{porta}", false),
                _ => ($"falha inesperada ({ex.GetType().Name})", false),
            };
            _logger.LogWarning("Caixa de e-mail do atendimento: {Protocolo} falhou ({Tipo}): {Motivo}",
                protocolo, ex.GetType().Name, motivo);
            throw new FalhaCaixaEmailException($"{protocolo}: {motivo}", permanente, ex);
        }
    }

    private static async Task EncerrarSemFalharAsync(Func<Task> encerrar)
    {
        try
        {
            await encerrar().WaitAsync(TimeSpan.FromSeconds(2));
        }
        catch (Exception)
        {
            // Dispose do cliente fecha o socket de qualquer jeito.
        }
    }

    /// <summary>INBOX aberta. <c>GetMessage</c> do MailKit usa BODY.PEEK: ler não marca como lido.</summary>
    private sealed class SessaoImap(ImapClient imap, TimeProvider relogio) : ISessaoCaixaEmail
    {
        public async Task<IReadOnlyList<string>> ListarNaoLidosAsync(int maximo, CancellationToken ct = default)
        {
            var uids = await imap.Inbox.SearchAsync(SearchQuery.NotSeen, ct);
            return uids.OrderBy(u => u.Id).Take(Math.Max(0, maximo)).Select(u => u.ToString()).ToList();
        }

        public async Task<EmailRecebido> BaixarAsync(string idNaCaixa, CancellationToken ct = default)
        {
            var uid = Uid(idNaCaixa);
            var agora = relogio.GetUtcNow().UtcDateTime;

            // E-mail gigante não entra inteiro na memória: fica o envelope e o aviso para abrir na caixa.
            var resumo = (await imap.Inbox.FetchAsync([uid],
                new FetchRequest(MessageSummaryItems.Size | MessageSummaryItems.Envelope), ct)).FirstOrDefault();
            if (resumo?.Size > LeitorMimeEmail.TetoEmailBytes && resumo.Envelope is not null)
                return LeitorMimeEmail.SoEnvelope(idNaCaixa, resumo.Envelope, resumo.Size.Value, agora);

            using var mime = await imap.Inbox.GetMessageAsync(uid, ct);
            return LeitorMimeEmail.Ler(idNaCaixa, mime, agora);
        }

        public Task MarcarLidoAsync(string idNaCaixa, CancellationToken ct = default) =>
            imap.Inbox.StoreAsync(Uid(idNaCaixa), new StoreFlagsRequest(StoreAction.Add, MessageFlags.Seen) { Silent = true }, ct);

        public async ValueTask DisposeAsync()
        {
            await EncerrarSemFalharAsync(() => imap.DisconnectAsync(true, CancellationToken.None));
            imap.Dispose();
        }

        private static UniqueId Uid(string id) =>
            UniqueId.TryParse(id, out var uid) ? uid : throw new ArgumentException("Id do e-mail na caixa inválido.", nameof(id));
    }
}
