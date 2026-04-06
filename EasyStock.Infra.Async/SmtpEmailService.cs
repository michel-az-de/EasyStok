using EasyStock.Application.Ports.Output;
using System.Net;
using System.Net.Mail;
using System.Net.Sockets;

namespace EasyStock.Infra.Async;

/// <summary>
/// Implementacao SMTP do servico de email.
/// Suporte a templates basicos, anexos e retry automatico em falhas transientes.
/// </summary>
public sealed class SmtpEmailService : IEmailService, IDisposable
{
    private readonly SmtpClient _smtpClient;
    private readonly string _fromEmail;
    private readonly string _fromName;
    private const int MaxRetries = 3;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(2);

    public SmtpEmailService(string host, int port, string username, string password, string fromEmail, string fromName, bool enableSsl = true)
    {
        _fromEmail = fromEmail;
        _fromName = fromName;
        _smtpClient = new SmtpClient(host, port)
        {
            Credentials = new NetworkCredential(username, password),
            EnableSsl = enableSsl,
            DeliveryMethod = SmtpDeliveryMethod.Network,
            UseDefaultCredentials = false
        };
    }

    public Task SendAsync(string to, string subject, string body, bool isHtml = false) =>
        SendAsync(new[] { to }, subject, body, isHtml);

    public Task SendAsync(string to, string subject, string body, IEnumerable<EmailAttachment> attachments, bool isHtml = false) =>
        SendAsync(new[] { to }, subject, body, attachments, isHtml);

    public Task SendAsync(IEnumerable<string> to, string subject, string body, bool isHtml = false) =>
        SendAsync(to, subject, body, Enumerable.Empty<EmailAttachment>(), isHtml);

    public async Task SendAsync(IEnumerable<string> to, string subject, string body, IEnumerable<EmailAttachment> attachments, bool isHtml = false)
    {
        Exception? lastException = null;
        for (var tentativa = 1; tentativa <= MaxRetries; tentativa++)
        {
            try
            {
                await EnviarInternamenteAsync(to, subject, body, attachments, isHtml);
                return;
            }
            catch (SmtpException ex) when (EhFalhaTransiente(ex))
            {
                lastException = ex;
                if (tentativa < MaxRetries)
                    await Task.Delay(RetryDelay * tentativa);
            }
        }

        throw lastException!;
    }

    private async Task EnviarInternamenteAsync(IEnumerable<string> to, string subject, string body, IEnumerable<EmailAttachment> attachments, bool isHtml)
    {
        using var mailMessage = new MailMessage
        {
            From = new MailAddress(_fromEmail, _fromName),
            Subject = subject,
            Body = body,
            IsBodyHtml = isHtml
        };
        foreach (var recipient in to)
        {
            mailMessage.To.Add(recipient);
        }
        foreach (var attachment in attachments)
        {
            var mailAttachment = new Attachment(new MemoryStream(attachment.Content), attachment.FileName, attachment.ContentType);
            mailMessage.Attachments.Add(mailAttachment);
        }
        await _smtpClient.SendMailAsync(mailMessage);
    }

    private static bool EhFalhaTransiente(SmtpException ex) =>
        ex.StatusCode is SmtpStatusCode.ServiceNotAvailable
            or SmtpStatusCode.MailboxBusy
            or SmtpStatusCode.MailboxUnavailable
            or SmtpStatusCode.InsufficientStorage
        || ex.InnerException is SocketException or IOException;

    public Task SendTemplateAsync(string to, string subject, string templateName, object model, bool isHtml = true)
    {
        // Implementacao basica - em producao usar template engine como Razor ou Handlebars
        var body = $"Template: {templateName}\n\nModel: {System.Text.Json.JsonSerializer.Serialize(model)}";
        return SendAsync(to, subject, body, isHtml);
    }

    public void Dispose()
    {
        _smtpClient.Dispose();
    }
}
