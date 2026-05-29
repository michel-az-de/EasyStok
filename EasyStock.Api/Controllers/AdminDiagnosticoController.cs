using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Infra.Notifications.WhatsApp;

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
public class AdminDiagnosticoController(IEmailService emailService, MetaCloudWhatsAppProvider whatsAppMeta) : EasyStockControllerBase
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
            ? string.Format(
                EasyStock.Api.Data.EmailTemplateLoader.LoadBody("diagnostico_teste_email"),
                DateTime.UtcNow.ToString("O"))
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

    public sealed record EnviarWhatsAppTesteRequest(
        string Destino,
        string Modo,
        string? Corpo,
        string? TemplateName,
        IReadOnlyList<string>? Vars,
        string? LanguageCode);
    public sealed record EnviarWhatsAppTesteResult(bool Sucesso, string Provedor, string? Erro, long DuracaoMs);

    /// <summary>
    /// Onda 2.1 — envia mensagem WhatsApp de teste via Meta Cloud API.
    /// Modo "text" usa corpo livre (so funciona dentro da janela de 24h pos-resposta).
    /// Modo "template" usa template aprovado na Meta com vars posicionais (proativa, fora da janela 24h).
    /// </summary>
    [HttpPost("whatsapp/teste")]
    public async Task<IActionResult> EnviarWhatsAppTeste([FromBody] EnviarWhatsAppTesteRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Destino))
            return DataBadRequest("Destino obrigatorio (formato E.164, ex: +5511999999999).");
        var modo = (req.Modo ?? "text").ToLowerInvariant();

        if (modo == "text")
        {
            if (string.IsNullOrWhiteSpace(req.Corpo)) return DataBadRequest("Corpo obrigatorio em modo=text.");
            var msg = new MensagemPronta(
                OutboxId: Guid.NewGuid(),
                EmpresaId: Guid.Empty,
                Destinatario: req.Destino.Trim(),
                Assunto: "Teste diagnostico",
                Corpo: req.Corpo,
                Canal: EasyStock.Domain.Enums.Notifications.CanalNotificacao.WhatsApp,
                Categoria: EasyStock.Domain.Enums.Notifications.CategoriaConteudoNotificacao.Transacional);
            var r = await whatsAppMeta.EnviarAsync(msg, ct);
            return DataOk(new EnviarWhatsAppTesteResult(r.Sucesso, r.ProviderUsado ?? "meta", r.ErroDetalhado, r.DuracaoMs));
        }

        if (modo == "template")
        {
            if (string.IsNullOrWhiteSpace(req.TemplateName)) return DataBadRequest("TemplateName obrigatorio em modo=template.");
            var r = await whatsAppMeta.EnviarTemplateAsync(
                destino: req.Destino.Trim(),
                templateName: req.TemplateName.Trim(),
                vars: req.Vars ?? Array.Empty<string>(),
                languageCode: string.IsNullOrWhiteSpace(req.LanguageCode) ? "pt_BR" : req.LanguageCode!.Trim(),
                ct: ct);
            return DataOk(new EnviarWhatsAppTesteResult(r.Sucesso, r.ProviderUsado ?? "meta:template", r.ErroDetalhado, r.DuracaoMs));
        }

        return DataBadRequest("Modo deve ser 'text' ou 'template'.");
    }
}
