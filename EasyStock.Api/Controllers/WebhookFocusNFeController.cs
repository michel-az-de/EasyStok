using System.Text;
using System.Text.Json;
using EasyStock.Api.Http;
using EasyStock.Application.UseCases.Fiscal.ProcessarWebhookFocusNFe;
using EasyStock.Infra.Integrations.Fiscal.FocusNFe;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
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
    [SwaggerOperation(Summary = "Recebe notificacao de status change da NFC-e (webhook Focus)")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [HttpPost("nfe")]
    public async Task<IActionResult> ReceberWebhook(CancellationToken ct)
    {
        // Le body como bytes (necessario para HMAC + nao desperdica ao deserializar 2x)
        using var ms = new MemoryStream();
        await Request.Body.CopyToAsync(ms, ct);
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
