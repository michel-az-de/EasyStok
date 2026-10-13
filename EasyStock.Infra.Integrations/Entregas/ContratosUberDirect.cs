using System.Text.Json.Serialization;

namespace EasyStock.Infra.Integrations.Entregas;

// Subconjunto usado pelo adapter; fonte: developer.uber.com/docs/deliveries/get-started
// Uma entrega tem um destino. Batch recebido no webhook não autoriza criar múltiplos destinos.
public sealed record UberCotacaoRequest(
    [property: JsonPropertyName("pickup_address")] string PickupAddress,
    [property: JsonPropertyName("dropoff_address")] string DropoffAddress,
    [property: JsonPropertyName("pickup_latitude")] double PickupLatitude,
    [property: JsonPropertyName("pickup_longitude")] double PickupLongitude,
    [property: JsonPropertyName("dropoff_latitude")] double DropoffLatitude,
    [property: JsonPropertyName("dropoff_longitude")] double DropoffLongitude,
    [property: JsonPropertyName("pickup_ready_dt")] DateTimeOffset PickupReadyDt,
    [property: JsonPropertyName("dropoff_deadline_dt")] DateTimeOffset DropoffDeadlineDt);

public sealed record UberCotacaoResponse(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("fee")] int Fee,
    [property: JsonPropertyName("currency")] string Currency,
    [property: JsonPropertyName("expires")] DateTimeOffset Expires,
    [property: JsonPropertyName("dropoff_eta")] DateTimeOffset DropoffEta);

public sealed record UberItem(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("quantity")] int Quantity,
    [property: JsonPropertyName("size")] string Size = "small");

public sealed record UberEntregaRequest(
    [property: JsonPropertyName("quote_id")] string QuoteId,
    [property: JsonPropertyName("pickup_address")] string PickupAddress,
    [property: JsonPropertyName("pickup_name")] string PickupName,
    [property: JsonPropertyName("pickup_phone_number")] string PickupPhoneNumber,
    [property: JsonPropertyName("dropoff_address")] string DropoffAddress,
    [property: JsonPropertyName("dropoff_name")] string DropoffName,
    [property: JsonPropertyName("dropoff_phone_number")] string DropoffPhoneNumber,
    [property: JsonPropertyName("manifest_items")] IReadOnlyList<UberItem> ManifestItems,
    [property: JsonPropertyName("idempotency_key")] string IdempotencyKey,
    [property: JsonPropertyName("pickup_ready_dt")] DateTimeOffset PickupReadyDt,
    [property: JsonPropertyName("dropoff_deadline_dt")] DateTimeOffset DropoffDeadlineDt,
    [property: JsonPropertyName("undeliverable_action")] string UndeliverableAction = "return");

public sealed record UberEntregaResponse(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("fee")] int Fee,
    [property: JsonPropertyName("currency")] string Currency,
    [property: JsonPropertyName("tracking_url")] string? TrackingUrl,
    [property: JsonPropertyName("live_mode")] bool LiveMode);
