using System.Security.Cryptography;
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
            logger.LogWarning("Webhook WhatsApp: verificação recusada (mode={Mode}).", mode);
            return Forbid();
        }

        return Content(challenge ?? "", "text/plain");
    }

    [HttpPost]
    [EnableRateLimiting("public-post")]
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

        await processarUseCase.ExecuteAsync(rawBody, ct);

        // Sempre 200 quando a assinatura é válida: a Meta reenvia em não-200, e o
        // reprocessamento é seguro pela idempotência por wamid dentro do use case.
        return Ok();
    }

    private bool AssinaturaValida(string rawBody, string appSecret)
    {
        if (!Request.Headers.TryGetValue("X-Hub-Signature-256", out var header))
            return false;

        var recebida = header.ToString();
        const string prefixo = "sha256=";
        if (!recebida.StartsWith(prefixo, StringComparison.OrdinalIgnoreCase))
            return false;
        recebida = recebida[prefixo.Length..];

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(appSecret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(rawBody));
        var esperada = Convert.ToHexString(hash).ToLowerInvariant();

        return FixedTimeEquals(esperada, recebida.ToLowerInvariant());
    }

    private static bool FixedTimeEquals(string a, string b)
    {
        var ba = Encoding.UTF8.GetBytes(a);
        var bb = Encoding.UTF8.GetBytes(b);
        return ba.Length == bb.Length && CryptographicOperations.FixedTimeEquals(ba, bb);
    }
}
