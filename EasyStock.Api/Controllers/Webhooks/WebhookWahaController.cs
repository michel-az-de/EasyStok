using System.Security.Cryptography;
using System.Text;
using EasyStock.Application.UseCases.Atendimento.Webhook;
using EasyStock.Infra.Integrations.WhatsApp.Waha;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace EasyStock.Api.Controllers.Webhooks;

/// <summary>
/// Webhook da ponte provisória do WhatsApp pelo WAHA (#2072, ADR-0062). Só responde com
/// <c>Atendimento:WhatsApp:Cliente=waha</c>; no resto, 404. O corpo é assinado com HMAC sha512 em
/// <c>X-Webhook-Hmac</c>, traduzido para o formato da Meta e processado pelo mesmo use case do webhook da Meta.
/// Não-200 faz o WAHA reenviar; o use case pula pelo id o que já gravou.
/// </summary>
[ApiController]
[Route("api/webhooks/waha")]
[AllowAnonymous]
[IgnoreAntiforgeryToken] // Servidor-a-servidor autenticado por HMAC, sem cookie: CSRF não se aplica.
public class WebhookWahaController(
    ProcessarEventoWhatsAppUseCase processarUseCase,
    TradutorEventoWaha tradutor,
    IOptions<WahaOptions> wahaOptions,
    ILogger<WebhookWahaController> logger) : ControllerBase
{
    public const string HeaderAssinatura = "X-Webhook-Hmac";

    [HttpPost]
    [EnableRateLimiting("webhook-meta")]
    public async Task<IActionResult> Receber(CancellationToken ct)
    {
        var opcoes = wahaOptions.Value;
        if (!opcoes.Ligada) return NotFound();

        using var memoria = new MemoryStream();
        await Request.Body.CopyToAsync(memoria, ct);
        var corpo = memoria.ToArray();

        if (!AssinaturaValida(Request.Headers[HeaderAssinatura].ToString(), corpo, opcoes.HmacKey))
        {
            logger.LogWarning("Webhook WAHA: assinatura ausente ou inválida.");
            return Forbid();
        }

        var corpoMeta = await tradutor.TraduzirAsync(Encoding.UTF8.GetString(corpo), ct);
        if (corpoMeta is null) return Ok();

        var completo = await processarUseCase.ExecuteAsync(corpoMeta, ct);
        return completo ? Ok() : StatusCode(StatusCodes.Status503ServiceUnavailable);
    }

    /// <summary>HMAC sha512 do corpo bruto, em hexadecimal, comparado em tempo constante. Sem segredo, nada passa.</summary>
    public static bool AssinaturaValida(string? assinatura, byte[] corpo, string? segredo)
    {
        if (string.IsNullOrEmpty(segredo) || string.IsNullOrWhiteSpace(assinatura)) return false;

        var esperado = HMACSHA512.HashData(Encoding.UTF8.GetBytes(segredo), corpo);
        byte[] recebido;
        try
        {
            recebido = Convert.FromHexString(assinatura.Trim());
        }
        catch (FormatException)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(esperado, recebido);
    }
}
