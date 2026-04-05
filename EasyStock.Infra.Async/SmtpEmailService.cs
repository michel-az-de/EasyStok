using EasyStock.Application.Ports.Output;
using System.Net;
using System.Net.Mail;

namespace EasyStock.Infra.Async;

/// <summary>
/// Implementação SMTP do serviço de email.
/// Suporte a templates básicos e anexos.
/// </summary>
public sealed class SmtpEmailService : IEmailService, IDisposable
{
    private readonly SmtpClient _smtpClient;
    private readonly string _fromEmail;
    private readonly string _fromName;

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

    public async Task SendAsync(string to, string subject, string body, bool isHtml = false)
    {
        await SendAsync(new[] { to }, subject, body, isHtml);
    }

    public async Task SendAsync(string to, string subject, string body, IEnumerable<EmailAttachment> attachments, bool isHtml = false)
    {
        await SendAsync(new[] { to }, subject, body, attachments, isHtml);
    }

    public async Task SendAsync(IEnumerable<string> to, string subject, string body, bool isHtml = false)
    {
        await SendAsync(to, subject, body, Enumerable.Empty<EmailAttachment>(), isHtml);
    }

    public async Task SendAsync(IEnumerable<string> to, string subject, string body, IEnumerable<EmailAttachment> attachments, bool isHtml = false)
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

    public Task SendTemplateAsync(string to, string subject, string templateName, object model, bool isHtml = true)
    {
        // Implementação básica - em produção usar template engine como Razor ou Handlebars
        var body = $"Template: {templateName}\n\nModel: {System.Text.Json.JsonSerializer.Serialize(model)}";
        return SendAsync(to, subject, body, isHtml);
    }

    public void Dispose()
    {
        _smtpClient.Dispose();
    }
}