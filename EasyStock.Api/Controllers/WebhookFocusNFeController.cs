using System.Text.Json;
using EasyStock.Api.Http;
using EasyStock.Application.UseCases.Fiscal.ProcessarWebhookFocusNFe;
using EasyStock.Infra.Integrations.Fiscal.FocusNFe;
using EasyStock.Infra.Integrations.Fiscal.FocusNFe.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Swashbuckle.AspNetCore.Annotations;

namespace EasyStock.Api.Controllers;

[SwaggerTag("Webhook Focus NFe")]
[ApiController]
[Route("api/webhooks/focus-nfe")]
[AllowAnonymous]
[EnableRateLimiting("webhook-focus-nfe")]
public sealed class WebhookFocusNFeController(
    FocusNFeWebhookValidator validator,
    ProcessarWebhookFocusNFeUseCase useCase,
    ILogger<WebhookFocusNFeController> log) : EasyStockControllerBase
{
    private static readonly JsonSerializerOptions JsonOpts =
        new(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true };

    [HttpPost]
    [SwaggerOperation(Summary = "Recebe notificações assíncronas do Focus NFe")]
    public async Task<IActionResult> Receber(CancellationToken ct)
    {
        Request.EnableBuffering();
        using var reader = new StreamReader(Request.Body, leaveOpen: true);
        var body = await reader.ReadToEndAsync(ct);
        Request.Body.Position = 0;

        var signature = Request.Headers["X-Focus-Signature"].FirstOrDefault();
        if (!validator.ValidarHmac(body, signature))
        {
            log.LogWarning("Webhook Focus assinatura inválida.");
            return Unauthorized();
        }

        FocusNFeWebhookPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<FocusNFeWebhookPayload>(body, JsonOpts);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Webhook Focus payload inválido.");
            return BadRequest();
        }

        if (payload is null || string.IsNullOrEmpty(payload.ChaveNFe))
            return BadRequest();

        var cmd = new ProcessarWebhookFocusNFeCommand(
            ChaveAcesso: payload.ChaveNFe,
            Status: payload.Status ?? "",
            Protocolo: payload.Protocolo,
            DhEvento: ParseDate(payload.DataEvento),
            XmlEvento: payload.Xml,
            Codigo: payload.Codigo ?? payload.MensagemSefaz,
            Motivo: payload.Motivo,
            CorrelationId: HttpContext.TraceIdentifier);

        var resultado = await useCase.ExecuteAsync(cmd);

        // Sempre 200 quando processado (mesmo no-op). Erros internos retornam
        // 500 e Focus retentará — comportamento desejado.
        return Ok(new { processado = resultado.Processado });
    }

    private static DateTime? ParseDate(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        return DateTime.TryParse(raw, out var dt) ? dt.ToUniversalTime() : null;
    }
}
