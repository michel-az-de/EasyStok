using System.Diagnostics;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Notifications;
using SendGrid;
using SendGrid.Helpers.Mail;

namespace EasyStock.Infra.Async;

/// <summary>
/// Onda 1.3 — implementacao SendGrid (HTTP v3) do servico de email.
/// Selecionado via configuracao Email:Provider=sendgrid + SendGrid:ApiKey
/// (passados via construtor por <see cref="ServiceCollectionExtensions"/>).
///
/// <para>
/// Diferenca vs SMTP: deliverability superior, retries automaticos no proprio
/// SDK, suporte nativo a sandbox mode (testes sem enviar para destinatario real)
/// e webhook de bounce/spam/dropped. Bounce handling esta deferido — a entidade
/// de bloqueio por destinatario precisa decisao de modelagem separada.
/// </para>
/// </summary>
public sealed class SendGridEmailService : IEmailService
{
    private readonly ISendGridClient _client;
    private readonly string _fromEmail;
    private readonly string? _fromName;
    private readonly bool _sandbox;

    public SendGridEmailService(string apiKey, string fromEmail, string? fromName, bool sandbox = false)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new ArgumentException("SendGrid:ApiKey eh obrigatorio.", nameof(apiKey));

        _client = new SendGridClient(apiKey);
        _fromEmail = fromEmail;
        _fromName = fromName;
        _sandbox = sandbox;
    }

    public Task SendAsync(string to, string subject, string body, bool isHtml = false) =>
        SendAsync(new[] { to }, subject, body, isHtml);

    public Task SendAsync(string to, string subject, string body, IEnumerable<EmailAttachment> attachments, bool isHtml = false) =>
        SendAsync(new[] { to }, subject, body, attachments, isHtml);

    public Task SendAsync(IEnumerable<string> to, string subject, string body, bool isHtml = false) =>
        SendAsync(to, subject, body, Enumerable.Empty<EmailAttachment>(), isHtml);

    public Task SendAsync(IEnumerable<string> to, string subject, string body, IEnumerable<EmailAttachment> attachments, bool isHtml = false) =>
        EnviarHttpAsync(to, subject, body, attachments, isHtml, CancellationToken.None);

    /// <summary>
    /// N3 (#1351): o SendGrid usa um remetente so (<c>SendGrid:FromEmail</c>), entao a categoria
    /// <see cref="RemetenteEmail.Seguranca"/> nao muda a caixa aqui. A separacao por categoria vale para o SMTP.
    /// 4xx do SendGrid (menos 408 e 429) e falha permanente; o resto e transitorio. O detalhe leva so o status, nunca o
    /// corpo da resposta, que pode repetir o endereco.
    /// </summary>
    public async Task<ResultadoEnvio> EnviarAsync(MensagemEmail mensagem, CancellationToken ct = default)
    {
        var cronometro = Stopwatch.StartNew();
        try
        {
            await EnviarHttpAsync([mensagem.Destinatario], mensagem.Assunto, mensagem.Corpo, mensagem.Anexos ?? [], mensagem.Html, ct);
            return new ResultadoEnvio(Sucesso: true, ProviderUsado: "sendgrid", DuracaoMs: cronometro.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            ct.ThrowIfCancellationRequested();

            if (ex is SendGridHttpException http)
            {
                var permanente = http.StatusCode is >= 400 and < 500 and not 408 and not 429;
                return new ResultadoEnvio(
                    Sucesso: false,
                    ProviderUsado: "sendgrid",
                    ErroDetalhado: $"SendGrid HTTP {http.StatusCode}",
                    StatusHttp: http.StatusCode,
                    DuracaoMs: cronometro.ElapsedMilliseconds,
                    FalhaPermanente: permanente);
            }

            return new ResultadoEnvio(
                Sucesso: false,
                ProviderUsado: "sendgrid",
                ErroDetalhado: ex.GetType().Name,
                DuracaoMs: cronometro.ElapsedMilliseconds);
        }
    }

    private async Task EnviarHttpAsync(IEnumerable<string> to, string subject, string body, IEnumerable<EmailAttachment> attachments, bool isHtml, CancellationToken ct)
    {
        var from = new EmailAddress(_fromEmail, _fromName);
        var tos = to.Select(t => new EmailAddress(t)).ToList();
        if (tos.Count == 0) throw new ArgumentException("Destinatario obrigatorio.", nameof(to));

        var plainText = isHtml ? null : body;
        var htmlContent = isHtml ? body : null;
        var msg = MailHelper.CreateSingleEmailToMultipleRecipients(from, tos, subject, plainText, htmlContent);

        foreach (var attachment in attachments)
        {
            msg.AddAttachment(attachment.FileName, Convert.ToBase64String(attachment.Content), attachment.ContentType);
        }

        // Sandbox mode: requisicao eh validada mas nao envia. Util pra staging
        // (testar templates/SPF/DKIM sem spam pro destinatario real).
        if (_sandbox)
        {
            msg.MailSettings = new MailSettings { SandboxMode = new SandboxMode { Enable = true } };
        }

        var response = await _client.SendEmailAsync(msg, ct);
        if ((int)response.StatusCode >= 400)
        {
            var detalhe = await response.Body.ReadAsStringAsync(ct);
            throw new SendGridHttpException(
                (int)response.StatusCode,
                $"SendGrid retornou HTTP {(int)response.StatusCode}: {detalhe}");
        }
    }

    /// <summary>Mantem a mensagem de antes (<see cref="InvalidOperationException"/>) e leva o status para a classificacao.</summary>
    private sealed class SendGridHttpException(int statusCode, string mensagem) : InvalidOperationException(mensagem)
    {
        public int StatusCode { get; } = statusCode;
    }

    public Task SendTemplateAsync(string to, string subject, string templateName, object model, bool isHtml = true)
    {
        // Mesma implementacao basica do SmtpEmailService — quando migrarmos para
        // template engine (Razor/Scriban), troca aqui sem mexer nos chamadores.
        var body = $"Template: {templateName}\n\nModel: {System.Text.Json.JsonSerializer.Serialize(model)}";
        return SendAsync(to, subject, body, isHtml);
    }
}
