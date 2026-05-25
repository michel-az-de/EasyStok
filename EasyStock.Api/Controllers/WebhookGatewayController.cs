using System.Security.Cryptography;
using System.Text;
using EasyStock.Application.Ports.Output.Pagamentos;
using EasyStock.Application.Ports.Output.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EasyStock.Api.Controllers;

/// <summary>
/// Endpoint generico para webhooks de gateways de pagamento. Roteia por
/// <c>{provedor}</c> da rota e delega ao
/// <see cref="IGatewayWebhookProcessor"/> registrado.
///
/// <para>
/// Fluxo:
/// </para>
/// <list type="number">
///   <item>Le body bruto e calcula SHA-256 hash.</item>
///   <item>Resolve <see cref="IWebhookSignatureValidator"/> por provedor — se
///   inexistente, recusa com 404. Se assinatura invalida, 401.</item>
///   <item>Tenta registrar em <c>WebhookRecebido</c> (UNIQUE provedor+eventId).
///   Se ja existe (duplicado), retorna 200 idempotente sem reprocessar.</item>
///   <item>Resolve <see cref="IGatewayWebhookProcessor"/> e chama
///   <c>ProcessarAsync(rawBody, headers)</c>.</item>
///   <item>Marca processamento concluido (sucesso ou falha) para auditoria.</item>
/// </list>
///
/// <para>
/// O <c>WebhookPixController</c> legado em <c>/api/webhooks/pix</c> continua
/// funcionando para compatibilidade, mas no longo prazo gateways novos devem
/// integrar pelo endpoint generico.
/// </para>
/// </summary>
[ApiController]
[Route("api/webhooks")]
[AllowAnonymous]
public class WebhookGatewayController(
    IEnumerable<IGatewayWebhookProcessor> processors,
    IEnumerable<IWebhookSignatureValidator> validators,
    IWebhookRecebidoRepository webhookRepo,
    ILogger<WebhookGatewayController> logger) : ControllerBase
{
    private readonly IReadOnlyList<IGatewayWebhookProcessor> _processors = processors.ToList();
    private readonly IReadOnlyList<IWebhookSignatureValidator> _validators = validators.ToList();

    [HttpPost("{provedor}")]
    public async Task<IActionResult> Receber(string provedor, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(provedor)) return BadRequest();

        // Le o body bruto. EnableBuffering para permitir releitura se necessario.
        Request.EnableBuffering();
        using var reader = new StreamReader(Request.Body, Encoding.UTF8, leaveOpen: true);
        var rawBody = await reader.ReadToEndAsync(ct);
        Request.Body.Position = 0;

        // Headers como dicionario — case-sensitive segundo o provedor; usamos
        // direct lookup (cada validator/processor sabe o nome correto).
        var headers = Request.Headers
            .ToDictionary(h => h.Key, h => (string?)h.Value.ToString(), StringComparer.OrdinalIgnoreCase);

        // 1) Resolve validator
        var validator = _validators.FirstOrDefault(v =>
            string.Equals(v.Provedor, provedor, StringComparison.OrdinalIgnoreCase));
        if (validator is null)
        {
            logger.LogWarning("Webhook recebido para provedor desconhecido: {Provedor}", provedor);
            return NotFound();
        }

        // 2) Valida assinatura
        if (!validator.Validar(rawBody, headers))
        {
            logger.LogWarning("Webhook {Provedor}: assinatura invalida ou ausente.", provedor);
            return Unauthorized();
        }

        // 3) Hash + idempotencia
        var bodyHash = ComputeSha256(rawBody);
        // EventId estavel: alguns provedores enviam ID no payload (Stripe).
        // Para Efi (que nao envia), usamos o hash do body como fallback —
        // retries com mesmo payload dao mesmo hash → idempotente.
        var eventIdExterno = ExtrairEventIdExterno(rawBody, validator.Provedor) ?? bodyHash;

        var registro = await webhookRepo.TryRegistrarAsync(validator.Provedor, eventIdExterno, bodyHash, ct);
        if (registro is null)
        {
            // Duplicado — retorna 200 idempotente sem reprocessar.
            logger.LogInformation(
                "Webhook {Provedor} duplicado (eventId={EventId}). Retornando 200 idempotente.",
                validator.Provedor, eventIdExterno);
            return Ok();
        }

        // 4) Resolve processor
        var processor = _processors.FirstOrDefault(p =>
            string.Equals(p.Provedor, validator.Provedor, StringComparison.OrdinalIgnoreCase));
        if (processor is null)
        {
            logger.LogError("Webhook {Provedor}: validator existe mas processor nao registrado.", validator.Provedor);
            await webhookRepo.MarcarProcessadoAsync(registro.Id, sucesso: false, erro: "Processor nao registrado.", ct);
            return StatusCode(500);
        }

        // 5) Processa
        try
        {
            await processor.ProcessarAsync(rawBody, headers, ct);
            await webhookRepo.MarcarProcessadoAsync(registro.Id, sucesso: true, ct: ct);
            return Ok();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Webhook {Provedor}: falha no processamento.", validator.Provedor);
            await webhookRepo.MarcarProcessadoAsync(registro.Id, sucesso: false, erro: ex.Message, ct);
            return StatusCode(500);
        }
    }

    private static string ComputeSha256(string body)
    {
        var bytes = Encoding.UTF8.GetBytes(body ?? "");
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// Tenta extrair um event ID estavel do payload conforme o provedor.
    /// Stripe e Mercado Pago tem <c>id</c> no root; Efi nao envia, fica null
    /// e o caller usa hash do body como fallback.
    /// </summary>
    private static string? ExtrairEventIdExterno(string rawBody, string provedor)
    {
        if (string.IsNullOrWhiteSpace(rawBody)) return null;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(rawBody);
            var root = doc.RootElement;

            // Padrao 1: {"id": "evt_..."} no root (Stripe, Mercado Pago)
            if (root.ValueKind == System.Text.Json.JsonValueKind.Object
                && root.TryGetProperty("id", out var idEl)
                && idEl.ValueKind == System.Text.Json.JsonValueKind.String)
            {
                var id = idEl.GetString();
                if (!string.IsNullOrWhiteSpace(id)) return $"{provedor}:{id}";
            }
        }
        catch { /* JSON invalido — o processor vai descartar */ }
        return null;
    }
}
