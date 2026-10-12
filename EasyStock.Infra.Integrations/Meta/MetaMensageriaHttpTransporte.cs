using System.Net.Http.Json;
using System.Text.Json.Serialization;
using EasyStock.Infra.Integrations.Resilience;
using Microsoft.Extensions.Logging;
using Polly.Registry;

namespace EasyStock.Infra.Integrations.Meta;

/// <summary>
/// <c>POST /me/messages</c> com o page token (o "me" do token é a página; o Instagram ligado a ela usa
/// o mesmo endpoint com o IGSID). Só a chamada HTTP passa pelo Polly: erro de aplicação da Meta não é
/// reenviado sozinho.
/// </summary>
public sealed class MetaMensageriaHttpTransporte(
    HttpClient httpClient,
    ResiliencePipelineProvider<string> pipelineProvider,
    ILogger<MetaMensageriaHttpTransporte> logger) : IMetaMensageriaTransporte
{
    public async Task<string> EnviarAsync(object payload, CancellationToken ct = default)
    {
        var pipeline = pipelineProvider.GetPipeline(IntegrationCategories.WhatsApp);
        using var response = await pipeline.ExecuteAsync(
            async pollyCt => await httpClient.PostAsJsonAsync("me/messages", payload, pollyCt), ct);

        if (!response.IsSuccessStatusCode)
        {
            ErroEnvelope? envelope = null;
            try { envelope = await response.Content.ReadFromJsonAsync<ErroEnvelope>(cancellationToken: ct); }
            catch (Exception ex) when (ex is System.Text.Json.JsonException or NotSupportedException) { }

            var codigo = envelope?.Error?.Code ?? 0;
            var mensagem = envelope?.Error?.Message ?? $"Meta Send API retornou HTTP {(int)response.StatusCode}.";
            logger.LogWarning("Falha Meta Send API: codigo={Codigo} subcodigo={Subcodigo} mensagem={Mensagem}",
                codigo, envelope?.Error?.ErrorSubcode, mensagem);
            throw new MetaMensageriaException(codigo, envelope?.Error?.ErrorSubcode, mensagem);
        }

        var corpo = await response.Content.ReadFromJsonAsync<RespostaEnvio>(cancellationToken: ct);
        return string.IsNullOrEmpty(corpo?.MessageId)
            ? throw new MetaMensageriaException(0, null, "Meta retornou 2xx sem message_id.")
            : corpo.MessageId;
    }

    private sealed record RespostaEnvio([property: JsonPropertyName("message_id")] string? MessageId);

    private sealed record ErroEnvelope([property: JsonPropertyName("error")] ErroMeta? Error);

    private sealed record ErroMeta(
        [property: JsonPropertyName("code")] int Code,
        [property: JsonPropertyName("error_subcode")] int? ErrorSubcode,
        [property: JsonPropertyName("message")] string? Message);
}