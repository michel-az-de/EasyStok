using EasyStock.Application.Ports.Output;
using System.Net;
using System.Net.Mail;

namespace EasyStock.Infra.Async;

/// <summary>
/// Implementacao SMTP do servico de email.
/// Suporte a templates basicos e anexos. Uma tentativa por chamada: a falha sobe como
/// <see cref="SmtpException"/> e quem repete e o outbox de notificacoes (backoff de 1, 5 e 30 min), que classifica
/// SMTP 5xx como permanente e SMTP 4xx como transitorio. O laco de 3 tentativas que existia aqui, somado ao Polly do
/// canal e ao outbox, fazia ate 36 tentativas SMTP por mensagem e repetia o 550 (N2).
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

    public Task SendAsync(string to, string subject, string body, bool isHtml = false) =>
        SendAsync(new[] { to }, subject, body, isHtml);

    public Task SendAsync(string to, string subject, string body, IEnumerable<EmailAttachment> attachments, bool isHtml = false) =>
        SendAsync(new[] { to }, subject, body, attachments, isHtml);

    public Task SendAsync(IEnumerable<string> to, string subject, string body, bool isHtml = false) =>
        SendAsync(to, subject, body, Enumerable.Empty<EmailAttachment>(), isHtml);

    public Task SendAsync(IEnumerable<string> to, string subject, string body, IEnumerable<EmailAttachment> attachments, bool isHtml = false) =>
        EnviarInternamenteAsync(to, subject, body, attachments, isHtml);

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

        // Anexos: criamos stream+attachment dentro de try/catch para garantir
        // dispose se a construção do Attachment falhar (ContentType inválido etc).
        // Após Add em mailMessage.Attachments, o dispose do MailMessage cascateia.
        foreach (var attachment in attachments)
        {
            MemoryStream? stream = null;
            Attachment? mailAttachment = null;
            try
            {
                stream = new MemoryStream(attachment.Content);
                mailAttachment = new Attachment(stream, attachment.FileName, attachment.ContentType);
                mailMessage.Attachments.Add(mailAttachment);
                // Ownership transferido para o MailMessage neste ponto.
                stream = null;
                mailAttachment = null;
            }
            finally
            {
                mailAttachment?.Dispose();
                stream?.Dispose();
            }
        }

        await _smtpClient.SendMailAsync(mailMessage);
    }

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
