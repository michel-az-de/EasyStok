using EasyStock.Application.Ports.Output;
using System.Net;
using System.Net.Mail;
using System.Net.Sockets;
using System.Text.Json;

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

    private static bool EhFalhaTransiente(SmtpException ex) =>
        ex.StatusCode is SmtpStatusCode.ServiceNotAvailable
            or SmtpStatusCode.MailboxBusy
            or SmtpStatusCode.MailboxUnavailable
            or SmtpStatusCode.InsufficientStorage
        || ex.InnerException is SocketException or IOException;

    public Task SendTemplateAsync(string to, string subject, string templateName, object model, bool isHtml = true)
    {
        var modelJson = JsonSerializer.Serialize(model, new JsonSerializerOptions { WriteIndented = true });
        var body = isHtml
            ? $"""
              <!doctype html>
              <html lang="pt-BR">
              <body style="margin:0;background:#f4f7fb;font-family:Inter,Segoe UI,Arial,sans-serif;color:#0f172a;">
                <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="background:#f4f7fb;padding:32px 16px;">
                  <tr>
                    <td align="center">
                      <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="max-width:560px;background:#ffffff;border:1px solid #e2e8f0;border-radius:18px;overflow:hidden;">
                        <tr>
                          <td style="padding:24px 28px;background:linear-gradient(135deg,#312e81,#4f46e5 58%,#0891b2);color:#ffffff;">
                            <div style="font-size:12px;font-weight:800;letter-spacing:.18em;text-transform:uppercase;opacity:.75;">EasyStock</div>
                            <h1 style="margin:8px 0 0;font-size:22px;line-height:1.25;">{WebUtility.HtmlEncode(subject)}</h1>
                          </td>
                        </tr>
                        <tr>
                          <td style="padding:28px;color:#475569;font-size:14px;line-height:1.7;">
                            <p style="margin:0 0 16px;">Recebemos uma atualização relacionada a <strong>{WebUtility.HtmlEncode(templateName)}</strong>.</p>
                            <pre style="white-space:pre-wrap;word-break:break-word;background:#f8fafc;border:1px solid #e2e8f0;border-radius:12px;padding:14px;color:#334155;font-family:Consolas,Menlo,monospace;font-size:12px;">{WebUtility.HtmlEncode(modelJson)}</pre>
                            <p style="margin:18px 0 0;color:#64748b;font-size:13px;">Equipe EasyStock</p>
                          </td>
                        </tr>
                      </table>
                    </td>
                  </tr>
                </table>
              </body>
              </html>
              """
            : $"EasyStock - {subject}\n\nTemplate: {templateName}\n\nDados:\n{modelJson}\n\nEquipe EasyStock";
        return SendAsync(to, subject, body, isHtml);
    }

    public void Dispose()
    {
        _smtpClient.Dispose();
    }
}
