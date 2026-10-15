using System.Text;
using EasyStock.Application.UseCases.Notifications.Plataforma;
using EasyStock.Infra.Notifications.Options;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace EasyStock.Api.Controllers.Webhooks;

/// <summary>
/// Webhook do número de plataforma da Meta (N6). A Meta o entrega aqui por override do número
/// (<c>override_callback_uri</c>), com mensagens e status do 2º número da WABA. <c>GET</c> verifica o endpoint com o
/// verify token PRÓPRIO da plataforma (<c>Notifications:WhatsApp:Plataforma:VerifyToken</c>); <c>POST</c> confere a
/// assinatura <c>X-Hub-Signature-256</c> com o <c>AppSecret</c> do app (o mesmo do atendimento). Corpo lido bruto antes
/// do model binding, necessário ao HMAC. Não depende do atendimento.
/// </summary>
[ApiController]
[Route("api/webhooks/whatsapp-plataforma")]
[AllowAnonymous]
[IgnoreAntiforgeryToken] // Servidor a servidor, autenticado por HMAC, sem cookie: CSRF não se aplica.
public class WebhookWhatsAppPlataformaController(
    ProcessarWebhookWhatsAppPlataformaUseCase processarUseCase,
    IOptions<MetaCloudWhatsAppOptions> metaOptions,
    IOptions<WhatsAppPlataformaOptions> plataformaOptions,
    ILogger<WebhookWhatsAppPlataformaController> logger) : ControllerBase
{
    [HttpGet]
    public IActionResult Verificar(
        [FromQuery(Name = "hub.mode")] string? mode,
        [FromQuery(Name = "hub.verify_token")] string? verifyToken,
        [FromQuery(Name = "hub.challenge")] string? challenge)
    {
        var tokenConfigurado = plataformaOptions.Value.VerifyToken;

        if (!string.Equals(mode, "subscribe", StringComparison.Ordinal)
            || string.IsNullOrEmpty(tokenConfigurado)
            || !AssinaturaWebhookMeta.IguaisEmTempoConstante(verifyToken ?? "", tokenConfigurado))
        {
            logger.LogWarning("Webhook de plataforma: verificação recusada.");
            return Forbid();
        }

        return Content(challenge ?? "", "text/plain");
    }

    [HttpPost]
    [EnableRateLimiting("webhook-meta")]
    public async Task<IActionResult> Receber(CancellationToken ct)
    {
        Request.EnableBuffering();
        using var reader = new StreamReader(Request.Body, Encoding.UTF8, leaveOpen: true);
        var rawBody = await reader.ReadToEndAsync(ct);
        Request.Body.Position = 0;

        var appSecret = metaOptions.Value.AppSecret;
        if (string.IsNullOrEmpty(appSecret)
            || !AssinaturaWebhookMeta.Valida(Request.Headers[AssinaturaWebhookMeta.Header].ToString(), rawBody, appSecret))
        {
            logger.LogWarning("Webhook de plataforma: assinatura ausente ou inválida.");
            return Forbid();
        }

        // 200 quando processou ou descartou de propósito; 503 só em falha transitória, para a Meta reenviar.
        var completo = await processarUseCase.ExecuteAsync(rawBody, ct);
        return completo ? Ok() : StatusCode(StatusCodes.Status503ServiceUnavailable);
    }
}
