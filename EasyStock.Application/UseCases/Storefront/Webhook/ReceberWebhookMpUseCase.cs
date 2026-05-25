using System.Text;
using System.Text.Json;
using EasyStock.Application.Ports.Output.Pagamentos;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Domain.Entities.Storefront;
using EasyStock.Domain.Exceptions;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.UseCases.Storefront.Webhook;

/// <summary>
/// Resultado do <see cref="ReceberWebhookMpUseCase"/> para o controller decidir
/// o status HTTP a devolver.
/// </summary>
public enum ReceberWebhookMpResultado
{
    /// <summary>HMAC OK e novo evento — INSERT realizado, retornar 200.</summary>
    Aceito = 0,

    /// <summary>HMAC OK mas eventoId duplicado — não houve INSERT, retornar 200 idempotente.</summary>
    Duplicado = 1,

    /// <summary>HMAC inválido — não persistiu nada, retornar 401.</summary>
    HmacInvalido = 2,

    /// <summary>Payload mal-formado (não-JSON ou faltam campos críticos) — retornar 400 ou 200 silencioso.</summary>
    PayloadInvalido = 3,
}

/// <summary>
/// Endpoint síncrono de webhook MP — receive-then-process (ADR-0006 §Receive).
///
/// <para>
/// Responsabilidades estritas: validar HMAC, extrair <c>eventoId</c> do payload,
/// tentar INSERT em <c>WebhookProcessado(status=Received)</c>. Em caso de
/// duplicate-key (MP retry, 100x normal), retorna <see cref="ReceberWebhookMpResultado.Duplicado"/>
/// sem reprocessar. SLA interno: <c>&lt; 200 ms</c> (MP timeout 22 s).
/// </para>
///
/// <para>
/// <strong>Não chama API externa</strong>. <strong>Não muda Pedido</strong>.
/// Tudo isso é responsabilidade do <see cref="ProcessarWebhookMpUseCase"/>
/// executado pelo background service.
/// </para>
/// </summary>
public sealed class ReceberWebhookMpUseCase(
    IWebhookProcessadoRepository webhookRepository,
    IMpWebhookSecretProvider secretProvider,
    MpHmacValidator hmacValidator,
    ILogger<ReceberWebhookMpUseCase> logger)
{
    public async Task<ReceberWebhookMpResultado> ExecuteAsync(
        ReceberWebhookMpInput input,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (input.PayloadRaw is null || input.PayloadRaw.Length == 0)
        {
            logger.LogWarning("webhook_mp_payload_vazio xRequestId={XRequestId}", input.XRequestId);
            return ReceberWebhookMpResultado.PayloadInvalido;
        }

        // ── 1) HMAC ─────────────────────────────────────────────────────────
        string secret;
        try
        {
            secret = secretProvider.ObterSecret();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "webhook_mp_secret_indisponivel xRequestId={XRequestId}", input.XRequestId);
            return ReceberWebhookMpResultado.HmacInvalido;
        }

        if (!hmacValidator.EhValido(input.PayloadRaw, input.AssinaturaHmac ?? string.Empty, secret))
        {
            // NUNCA log o secret nem a assinatura recebida em texto plano.
            logger.LogWarning(
                "webhook_mp_hmac_invalido xRequestId={XRequestId} payloadLen={Len}",
                input.XRequestId, input.PayloadRaw.Length);
            return ReceberWebhookMpResultado.HmacInvalido;
        }

        // ── 2) Extrair eventoId e tipo do payload ──────────────────────────
        string? eventoId;
        string? tipo;
        try
        {
            (eventoId, tipo) = ExtrairCamposChave(input.PayloadRaw);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex,
                "webhook_mp_payload_malformado xRequestId={XRequestId}", input.XRequestId);
            return ReceberWebhookMpResultado.PayloadInvalido;
        }

        // Preferência: x-request-id do MP (header) → senão data.id do payload.
        var eventoIdFinal = !string.IsNullOrWhiteSpace(input.XRequestId)
            ? input.XRequestId.Trim()
            : eventoId;

        if (string.IsNullOrWhiteSpace(eventoIdFinal))
        {
            logger.LogWarning(
                "webhook_mp_eventoId_ausente xRequestId={XRequestId}", input.XRequestId);
            return ReceberWebhookMpResultado.PayloadInvalido;
        }

        // ── 3) Dedup atômico via INSERT ────────────────────────────────────
        var payloadRawString = Encoding.UTF8.GetString(input.PayloadRaw);

        WebhookProcessado registro;
        try
        {
            registro = WebhookProcessado.Receber(
                provider: "mercadopago",
                eventoId: eventoIdFinal,
                tipo: tipo ?? "payment.updated",
                payloadRaw: payloadRawString);
        }
        catch (RegraDeDominioVioladaException ex)
        {
            logger.LogWarning(ex,
                "webhook_mp_factory_invalida xRequestId={XRequestId}", input.XRequestId);
            return ReceberWebhookMpResultado.PayloadInvalido;
        }

        var (inserido, _) = await webhookRepository.TentarRegistrarRecebidoAsync(registro, ct);

        if (inserido)
        {
            logger.LogInformation(
                "webhook_mp_recebido eventoId={EventoId} tipo={Tipo}",
                eventoIdFinal, tipo);
            return ReceberWebhookMpResultado.Aceito;
        }

        logger.LogInformation(
            "webhook_mp_duplicado eventoId={EventoId} tipo={Tipo}",
            eventoIdFinal, tipo);
        return ReceberWebhookMpResultado.Duplicado;
    }

    /// <summary>
    /// Extrai (eventoId, tipo) do payload bruto. Tolera variantes do shape MP:
    /// - <c>{ "type": "payment", "data": { "id": "..." } }</c> (novo)
    /// - <c>{ "action": "payment.updated", "data": { "id": "..." } }</c>
    /// </summary>
    private static (string? EventoId, string? Tipo) ExtrairCamposChave(byte[] payload)
    {
        using var doc = JsonDocument.Parse(payload);
        var root = doc.RootElement;

        // Preferência: "action" (ex: "payment.updated") > "type" (ex: "payment"). "action" é
        // mais específico e usado pelas variantes recentes do MP.
        string? tipo = null;
        if (root.TryGetProperty("action", out var actionEl) && actionEl.ValueKind == JsonValueKind.String)
            tipo = actionEl.GetString();
        else if (root.TryGetProperty("type", out var typeEl) && typeEl.ValueKind == JsonValueKind.String)
            tipo = typeEl.GetString();

        string? eventoId = null;
        if (root.TryGetProperty("data", out var dataEl)
            && dataEl.ValueKind == JsonValueKind.Object
            && dataEl.TryGetProperty("id", out var idEl))
        {
            eventoId = idEl.ValueKind switch
            {
                JsonValueKind.String => idEl.GetString(),
                JsonValueKind.Number => idEl.GetRawText(),
                _ => null,
            };
        }

        return (eventoId, tipo);
    }
}
