using System.Text.Json;
using EasyStock.Application.UseCases.Fiscal.ProcessarWebhookFocusNFe;
using EasyStock.Infra.Integrations.Fiscal.FocusNFe;
using Swashbuckle.AspNetCore.Annotations;

namespace EasyStock.Api.Controllers;

/// <summary>
/// Recebe webhooks de status change do Focus NFe. NAO autenticado por JWT —
/// validacao por HMAC-SHA256 no header <c>X-Focus-Signature</c>.
///
/// <para>
/// Use case <see cref="ProcessarWebhookFocusNFeUseCase"/> bypassa RLS automaticamente
/// porque webhook nao tem contexto de tenant — descobre pela chave de acesso.
/// </para>
/// </summary>
[SwaggerTag("Webhook Focus NFe (publico, validado por HMAC)")]
[AllowAnonymous]
[ApiController]
[Route("webhooks/focus")]
public class WebhookFocusNFeController(
    ProcessarWebhookFocusNFeUseCase useCase,
    FocusNFeWebhookValidator validator,
    ILogger<WebhookFocusNFeController> logger) : EasyStockControllerBase
{
    /// <summary>Limite defensivo do payload do webhook (Focus tipicamente envia &lt; 4KB).</summary>
    private const int MaxBodySizeBytes = 64 * 1024;

    [SwaggerOperation(Summary = "Recebe notificação de mudança de status da NFC-e (webhook Focus)")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status413PayloadTooLarge)]
    [HttpPost("nfe")]
    public async Task<IActionResult> ReceberWebhook(CancellationToken ct)
    {
        // Rejeita cedo se Content-Length declarar payload acima do limite (proteção DoS).
        if (Request.ContentLength.GetValueOrDefault() > MaxBodySizeBytes)
            return StatusCode(StatusCodes.Status413PayloadTooLarge);

        // Lê body como bytes (necessário para HMAC + não desperdiça ao deserializar 2x).
        // Limita stream para que requests sem Content-Length não estourem a memória.
        using var ms = new MemoryStream(capacity: 4096);
        var buffer = new byte[4096];
        int read;
        while ((read = await Request.Body.ReadAsync(buffer.AsMemory(), ct)) > 0)
        {
            if (ms.Length + read > MaxBodySizeBytes)
                return StatusCode(StatusCodes.Status413PayloadTooLarge);
            await ms.WriteAsync(buffer.AsMemory(0, read), ct);
        }
        var bodyBytes = ms.ToArray();

        var assinatura = Request.Headers["X-Focus-Signature"].FirstOrDefault();
        if (!validator.ValidarAssinatura(assinatura, bodyBytes))
        {
            logger.LogWarning("Webhook Focus rejeitado: HMAC invalido ou ausente. Header={Header}", assinatura);
            return Unauthorized();
        }

        FocusWebhookPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<FocusWebhookPayload>(bodyBytes, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            });
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Webhook Focus payload invalido (JSON malformado).");
            return DataBadRequest("Payload invalido.");
        }

        if (payload is null || string.IsNullOrWhiteSpace(payload.ChaveNfe))
        {
            return DataBadRequest("Payload sem chave_nfe.");
        }

        var resultado = await useCase.ExecuteAsync(new ProcessarWebhookFocusNFeCommand(
            ChaveAcesso: payload.ChaveNfe,
            StatusGateway: payload.Status ?? "desconhecido",
            ProtocoloAutorizacao: payload.Protocolo,
            MotivoRejeicao: payload.Mensagem ?? payload.MensagemSefaz,
            XmlAssinadoUrl: payload.CaminhoXmlNotaFiscal,
            DanfeUrl: payload.CaminhoDanfe,
            DataEvento: payload.DataEmissao ?? DateTime.UtcNow));

        return DataOk(new { aplicado = resultado.Aplicado, statusFinal = resultado.StatusFinal });
    }

    private sealed class FocusWebhookPayload
    {
        [System.Text.Json.Serialization.JsonPropertyName("chave_nfe")]
        public string? ChaveNfe { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("status")]
        public string? Status { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("status_sefaz")]
        public string? StatusSefaz { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("protocolo")]
        public string? Protocolo { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("mensagem_sefaz")]
        public string? MensagemSefaz { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("mensagem")]
        public string? Mensagem { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("caminho_xml_nota_fiscal")]
        public string? CaminhoXmlNotaFiscal { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("caminho_danfe")]
        public string? CaminhoDanfe { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("data_emissao")]
        public DateTime? DataEmissao { get; set; }
    }
}
