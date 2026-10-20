using System.Text.Json.Serialization;

namespace EasyStock.Infra.Integrations.Entregas;

// Fonte: developers.lalamove.com, API v3. IDs de 19 dígitos e valores monetários são strings.
public sealed record LalamoveEnvelope<T>([property: JsonPropertyName("data")] T Data);
public sealed record LalamoveCoordenadas(
    [property: JsonPropertyName("lat")] string Lat,
    [property: JsonPropertyName("lng")] string Lng);
public sealed record LalamoveParada(
    [property: JsonPropertyName("coordinates")] LalamoveCoordenadas Coordinates,
    [property: JsonPropertyName("address")] string Address,
    [property: JsonPropertyName("stopId"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? StopId = null);
/// <summary>Sem <c>scheduleAt</c> a cotação é para agora (#1882, preço do frete na hora do pedido).</summary>
public sealed record LalamoveCotacaoRequest(
    [property: JsonPropertyName("scheduleAt"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateTimeOffset? ScheduleAt,
    [property: JsonPropertyName("serviceType")] string ServiceType,
    [property: JsonPropertyName("language")] string Language,
    [property: JsonPropertyName("stops")] IReadOnlyList<LalamoveParada> Stops,
    [property: JsonPropertyName("isRouteOptimized")] bool IsRouteOptimized);
public sealed record LalamovePreco(
    [property: JsonPropertyName("total")] string Total,
    [property: JsonPropertyName("currency")] string Currency);
public sealed record LalamoveCotacaoResponse(
    [property: JsonPropertyName("quotationId")] string QuotationId,
    [property: JsonPropertyName("expiresAt")] DateTimeOffset ExpiresAt,
    [property: JsonPropertyName("stops")] IReadOnlyList<LalamoveParada> Stops,
    [property: JsonPropertyName("priceBreakdown")] LalamovePreco PriceBreakdown,
    [property: JsonPropertyName("isRouteOptimized")] bool IsRouteOptimized,
    [property: JsonPropertyName("distance")] LalamoveDistancia? Distance = null);
public sealed record LalamoveDistancia(
    [property: JsonPropertyName("value")] string Value,
    [property: JsonPropertyName("unit")] string Unit);
/// <summary><c>GET /v3/cities</c>: os tipos de veículo do mercado vêm daqui, não de enum fixo.</summary>
public sealed record LalamoveCidade(
    [property: JsonPropertyName("locode")] string Locode,
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("services")] IReadOnlyList<LalamoveServico>? Services);
public sealed record LalamoveServico(
    [property: JsonPropertyName("key")] string Key,
    [property: JsonPropertyName("description")] string? Description);
public sealed record LalamoveContato(
    [property: JsonPropertyName("stopId")] string StopId,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("phone")] string Phone);
public sealed record LalamoveEntregaRequest(
    [property: JsonPropertyName("quotationId")] string QuotationId,
    [property: JsonPropertyName("sender")] LalamoveContato Sender,
    [property: JsonPropertyName("recipients")] IReadOnlyList<LalamoveContato> Recipients,
    [property: JsonPropertyName("isPODEnabled")] bool IsPodEnabled = true);
public sealed record LalamoveEntregaResponse(
    [property: JsonPropertyName("orderId")] string OrderId,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("shareLink")] string? ShareLink,
    [property: JsonPropertyName("priceBreakdown")] LalamovePreco PriceBreakdown);
