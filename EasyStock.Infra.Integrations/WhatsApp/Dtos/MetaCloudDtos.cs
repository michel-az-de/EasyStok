using System.Text.Json.Serialization;

namespace EasyStock.Infra.Integrations.WhatsApp.Dtos;

internal sealed record MetaSendMessageResponse(List<MetaMessageId>? Messages);

internal sealed record MetaMessageId(string Id);

internal sealed record MetaErrorEnvelope(MetaError? Error);

internal sealed record MetaError(
    string Message,
    string Type,
    int Code,
    [property: JsonPropertyName("error_subcode")] int? ErrorSubcode,
    [property: JsonPropertyName("fbtrace_id")] string? FbTraceId);

internal sealed record MetaMediaMetadata(
    string Url,
    [property: JsonPropertyName("mime_type")] string MimeType,
    string Id);
