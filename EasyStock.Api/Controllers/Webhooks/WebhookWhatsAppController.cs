using System.Text;
using EasyStock.Application.UseCases.Atendimento.Webhook;
using EasyStock.Infra.Notifications.Options;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace EasyStock.Api.Controllers.Webhooks;

/// <summary>
/// Webhook da Meta Cloud API (S03). <c>GET</c> responde à verificação do endpoint (handshake de
/// configuração no Meta Developers); <c>POST</c> recebe mensagens e status de entrega.
/// Corpo lido bruto ANTES do model binding — necessário pro HMAC do <c>POST</c>.
/// </summary>
[ApiController]
[Route("api/webhooks/whatsapp")]
[AllowAnonymous]
[IgnoreAntiforgeryToken] // Webhook servidor-a-servidor autenticado por HMAC (X-Hub-Signature-256), sem cookie/sessão — CSRF não se aplica.
public class WebhookWhatsAppController(
    ProcessarEventoWhatsAppUseCase processarUseCase,
    IOptions<MetaCloudWhatsAppOptions> metaOptions,
    ILogger<WebhookWhatsAppController> logger) : ControllerBase
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
            || !FixedTimeEquals(verifyToken ?? "", tokenConfigurado))
        {
            logger.LogWarning("Webhook WhatsApp: verificação recusada (mode={Mode}).", SanitizarParaLog(mode));
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
        if (string.IsNullOrEmpty(appSecret) || !AssinaturaValida(rawBody, appSecret))
        {
            logger.LogWarning("Webhook WhatsApp: assinatura ausente ou inválida.");
            return Forbid();
        }

        var completo = await processarUseCase.ExecuteAsync(rawBody, ct);

        // Não-200 faz a Meta reenviar o payload inteiro; o reprocessamento é seguro porque o use
        // case pula pelo wamid o que já gravou. Só pede reenvio quando algo pode passar na próxima.
        return completo ? Ok() : StatusCode(StatusCodes.Status503ServiceUnavailable);
    }

    private bool AssinaturaValida(string rawBody, string appSecret) =>
        AssinaturaWebhookMeta.Valida(Request.Headers[AssinaturaWebhookMeta.Header].ToString(), rawBody, appSecret);

    private static bool FixedTimeEquals(string a, string b) => AssinaturaWebhookMeta.IguaisEmTempoConstante(a, b);

    /// <summary>Remove quebra de linha/retorno de carro de valor vindo da requisição antes de logar — evita log forging (entradas fabricadas no arquivo de log).</summary>
    private static string SanitizarParaLog(string? valor) =>
        (valor ?? "").Replace("\r", "").Replace("\n", "");
}
