using EasyStock.Application.UseCases.Storefront.Webhook;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace EasyStock.Api.Controllers.Storefront;

/// <summary>
/// Endpoint público de webhook MercadoPago — ADR-0006 (receive-then-process).
///
/// <para>
/// Auth: sem cookie, sem CORS. Segurança via HMAC SHA-256 header.
/// </para>
///
/// <para>
/// SLA: &lt; 200 ms p99. Processamento real é assíncrono via
/// <c>ProcessarWebhookMpPendenteBackgroundService</c>.
/// </para>
/// </summary>
[SwaggerTag("Storefront Webhooks")]
[ApiController]
[Route("api/storefront/webhooks")]
[AllowAnonymous]
public sealed class WebhookController(
    ReceberWebhookMpUseCase useCase) : ControllerBase
{
    private const int MaxBodyBytes = 64 * 1024; // 64 KiB — payload MP normal < 4 KiB

    /// <summary>
    /// Recebe um webhook do MercadoPago. Body cru lido com EnableBuffering, validado
    /// via HMAC, persiste em <c>webhook_processado(status=received)</c> e retorna 200
    /// imediato.
    /// </summary>
    [SwaggerOperation(
        Summary = "Webhook MercadoPago",
        Description = "Recebe e enfileira webhook MP. Validação HMAC obrigatória. " +
                      "Idempotente: retentativas do MP (mesmo x-request-id) retornam 200 sem reprocessar.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [HttpPost("mercadopago")]
    public async Task<IActionResult> ReceberMercadoPago(CancellationToken ct)
    {
        // ── Ler body cru ─────────────────────────────────────────────────────
        // EnableBuffering deve estar habilitado em Program.cs middleware antes deste handler.
        Request.EnableBuffering();
        Request.Body.Position = 0;

        using var ms = new MemoryStream();
        await Request.Body.CopyToAsync(ms, ct);
        var payloadRaw = ms.ToArray();

        if (payloadRaw.Length > MaxBodyBytes)
        {
            return StatusCode(StatusCodes.Status413PayloadTooLarge);
        }

        // ── Headers ──────────────────────────────────────────────────────────
        var assinatura = Request.Headers.TryGetValue("Authorization", out var authH)
            ? authH.ToString().Trim()
            : Request.Headers.TryGetValue("X-Signature", out var sigH)
                ? sigH.ToString().Trim()
                : string.Empty;

        // MP envia "Bearer XXXXX"? Não — para webhooks, usam token direto. Mas tolera prefixos comuns:
        if (assinatura.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            assinatura = assinatura[7..].Trim();
        else if (assinatura.StartsWith("sha256=", StringComparison.OrdinalIgnoreCase))
            assinatura = assinatura[7..].Trim();

        var xRequestId = Request.Headers.TryGetValue("x-request-id", out var xrH)
            ? xrH.ToString().Trim()
            : Request.Headers.TryGetValue("X-Request-Id", out var xrH2)
                ? xrH2.ToString().Trim()
                : string.Empty;

        var input = new ReceberWebhookMpInput(payloadRaw, assinatura, xRequestId);
        var resultado = await useCase.ExecuteAsync(input, ct);

        return resultado switch
        {
            ReceberWebhookMpResultado.Aceito => StatusCode(StatusCodes.Status200OK),
            ReceberWebhookMpResultado.Duplicado => StatusCode(StatusCodes.Status200OK),
            ReceberWebhookMpResultado.HmacInvalido => StatusCode(StatusCodes.Status401Unauthorized),
            // Payload inválido: retornamos 200 silencioso para que o MP não fique retentando
            // forever um payload que nunca será aceito. Logado em uso anterior do use case.
            ReceberWebhookMpResultado.PayloadInvalido => StatusCode(StatusCodes.Status200OK),
            _ => StatusCode(StatusCodes.Status200OK),
        };
    }
}
