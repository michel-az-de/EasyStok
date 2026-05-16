using EasyStock.Api.Http;
using EasyStock.Application.Ports.Output;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EasyStock.Api.Controllers;

/// <summary>
/// Onda 1.3 — diagnostico operacional do SuperAdmin (SaaS-side). Endpoints
/// usados para testar provedores externos (email, whatsapp, sms, push) sem
/// dependencia de evento real do outbox. Util para validar SendGrid/Meta/Twilio
/// em staging e producao.
/// </summary>
[ApiController]
[Route("api/admin/diagnostico")]
[Authorize(Policy = "SuperAdmin")]
public class AdminDiagnosticoController(IEmailService emailService) : EasyStockControllerBase
{
    public sealed record EnviarEmailTesteRequest(string Destino, string? Assunto, string? Corpo);
    public sealed record EnviarEmailTesteResult(bool Sucesso, string Provedor, string? Erro);

    /// <summary>
    /// Envia email de teste pelo provedor ativo (smtp|sendgrid|console).
    /// Para validar config + deliverability sem precisar publicar evento real.
    /// </summary>
    [HttpPost("email/teste")]
    public async Task<IActionResult> EnviarEmailTeste([FromBody] EnviarEmailTesteRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Destino) || !req.Destino.Contains('@'))
            return DataBadRequest("Destino invalido.");

        var assunto = string.IsNullOrWhiteSpace(req.Assunto)
            ? $"EasyStok — teste de email ({DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC)"
            : req.Assunto.Trim();
        var corpo = string.IsNullOrWhiteSpace(req.Corpo)
            ? $"<p>Email de teste enviado pelo diagnostico SuperAdmin em {DateTime.UtcNow:O}.</p>"
                + "<p>Se voce esta lendo isso, o provedor de email esta funcionando.</p>"
            : req.Corpo;

        var provedor = emailService.GetType().Name; // SmtpEmailService | SendGridEmailService | ConsoleEmailService
        try
        {
            await emailService.SendAsync(req.Destino.Trim(), assunto, corpo, isHtml: true);
            return DataOk(new EnviarEmailTesteResult(true, provedor, null));
        }
        catch (Exception ex)
        {
            return DataOk(new EnviarEmailTesteResult(false, provedor, ex.Message));
        }
    }
}
