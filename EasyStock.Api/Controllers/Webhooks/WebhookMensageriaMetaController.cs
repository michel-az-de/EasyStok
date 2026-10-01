using System.Text;
using EasyStock.Application.UseCases.Atendimento.Webhook;
using EasyStock.Infra.Notifications.Options;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace EasyStock.Api.Controllers.Webhooks;

/// <summary>
/// Webhook da Meta para Instagram Direct e Messenger (S35), assinado nos objetos <c>instagram</c> e
/// <c>page</c> da mesma app do WhatsApp: mesmo verify token no <c>GET</c> e mesmo App Secret no HMAC do
/// <c>POST</c>. Corpo lido bruto antes do model binding, para o HMAC.
/// </summary>
[ApiController]
[Route("api/webhooks/meta/mensageria")]
[AllowAnonymous]
[IgnoreAntiforgeryToken] // Servidor-a-servidor autenticado por HMAC (X-Hub-Signature-256), sem cookie — CSRF não se aplica.
public class WebhookMensageriaMetaController(
    ProcessarEventoMensageriaMetaUseCase processarUseCase,
    IOptions<MetaCloudWhatsAppOptions> metaOptions,
    ILogger<WebhookMensageriaMetaController> logger) : ControllerBase
{
    [HttpGet]
    public IActionResult Verificar(
        [FromQuery(Name = "hub.mode")] string? mode,
        [FromQuery(Name = "hub.verify_token")] string? verifyToken,
        [FromQuery(Name = "hub.challenge")] string? challenge)
    {
        var tokenConfigurado = metaOptions.Value.VerifyToken;
        if (!string.Equals(mode, "subscribe", StringComparison.Ordinal)
            || string.IsNullOrEmpty(tokenConfigurado)
            || !AssinaturaWebhookMeta.IguaisEmTempoConstante(verifyToken ?? "", tokenConfigurado))
        {
            logger.LogWarning("Webhook Meta (Instagram/Messenger): verificação recusada.");
            return Forbid();
        }

        return Content(challenge ?? "", "text/plain");
    }

    [HttpPost]
    // Mesmo balde do webhook do WhatsApp: a Meta entrega em rajada de poucos IPs (issue 1105/1285).
    [EnableRateLimiting("webhook-meta")]
    public async Task<IActionResult> Receber(CancellationToken ct)
    {
        Request.EnableBuffering();
        using var reader = new StreamReader(Request.Body, Encoding.UTF8, leaveOpen: true);
        var rawBody = await reader.ReadToEndAsync(ct);
        Request.Body.Position = 0;

        if (!AssinaturaWebhookMeta.Valida(Request.Headers[AssinaturaWebhookMeta.Header].ToString(), rawBody, metaOptions.Value.AppSecret))
        {
            logger.LogWarning("Webhook Meta (Instagram/Messenger): assinatura ausente ou inválida.");
            return Forbid();
        }

        // Não-200 faz a Meta reenviar; o mid já gravado é pulado.
        return await processarUseCase.ExecuteAsync(rawBody, ct) ? Ok() : StatusCode(StatusCodes.Status503ServiceUnavailable);
    }
}
