using System.Net.Http.Json;
using System.Text.Json.Serialization;
using EasyStock.Application.Ports.Output.Lookup;
using Microsoft.Extensions.Logging;

namespace EasyStock.Infra.Integrations.Geocoding;

/// <summary>
/// Adapter HTTP para a <see href="https://developers.google.com/maps/documentation/geocoding">Google
/// Geocoding API</see>: endereço → coordenada, base do módulo de entregas (issue #1217).
///
/// <para>
/// <strong>Contrato</strong>: igual ao do Nominatim, nunca lança. Status diferente de
/// <c>OK</c>, timeout, 4xx/5xx ou JSON inválido viram <see langword="null"/>. A chave vai
/// na query string, então a URL NUNCA é logada.
/// </para>
///
/// <para>
/// <strong>Confiança</strong> (ADR-0017): só é confiável com <c>location_type</c>
/// <c>ROOFTOP</c> ou <c>RANGE_INTERPOLATED</c> E componente <c>street_number</c> presente.
/// </para>
/// </summary>
public sealed class GoogleGeocodingClient : IGeocodingClient
{
    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly ILogger<GoogleGeocodingClient> _logger;

    public GoogleGeocodingClient(HttpClient http, string apiKey, ILogger<GoogleGeocodingClient> logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        _http = http;
        _apiKey = apiKey;
        _logger = logger;
    }

    public async Task<GeocodeResultado?> GeocodificarAsync(GeocodeQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var endereco = MontarEndereco(query);
        if (endereco is null) return null; // sem componente de endereço útil → não bate na rede

        var url = "geocode/json?" + string.Join('&',
            $"address={Uri.EscapeDataString(endereco)}",
            $"components={Uri.EscapeDataString("country:BR")}",
            "region=br",
            "language=pt-BR",
            $"key={Uri.EscapeDataString(_apiKey)}");

        try
        {
            using var resp = await _http.GetAsync(url, ct);
            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogDebug("Google Geocoding retornou HTTP {Status}", (int)resp.StatusCode);
                return null;
            }

            var corpo = await resp.Content.ReadFromJsonAsync<GoogleResposta>(ct);
            if (corpo is null || corpo.Status != "OK")
            {
                // REQUEST_DENIED/OVER_QUERY_LIMIT merecem Warning: indicam chave/cota, não endereço ruim.
                if (corpo?.Status is "REQUEST_DENIED" or "OVER_QUERY_LIMIT" or "OVER_DAILY_LIMIT")
                    _logger.LogWarning("Google Geocoding status {Status}", corpo.Status);
                return null;
            }

            var primeiro = corpo.Results is { Count: > 0 } ? corpo.Results[0] : null;
            var local = primeiro?.Geometry?.Location;
            if (local is null) return null;

            var granular = primeiro!.Geometry!.LocationType is "ROOFTOP" or "RANGE_INTERPOLATED";
            var temNumero = primeiro.AddressComponents?.Any(c => c.Types?.Contains("street_number") == true) == true;

            return new GeocodeResultado(local.Lat, local.Lng, granular && temNumero);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger.LogDebug("Google Geocoding timeout");
            return null;
        }
        catch (HttpRequestException ex)
        {
            // Não loga a exceção inteira: a mensagem pode carregar a URL com a chave.
            _logger.LogDebug("Google Geocoding HTTP falhou: {Status}", ex.StatusCode);
            return null;
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException or NotSupportedException)
        {
            _logger.LogDebug("Google Geocoding JSON inválido");
            return null;
        }
    }

    /// <summary>
    /// Endereço em linha única no formato brasileiro. <see langword="null"/> se não há
    /// logradouro, cidade nem CEP.
    /// </summary>
    private static string? MontarEndereco(GeocodeQuery q)
    {
        var temEndereco = !string.IsNullOrWhiteSpace(q.Logradouro)
                          || !string.IsNullOrWhiteSpace(q.Cidade)
                          || !string.IsNullOrWhiteSpace(q.Cep);
        if (!temEndereco) return null;

        var rua = string.Join(", ", new[] { q.Logradouro, q.Numero }.Where(s => !string.IsNullOrWhiteSpace(s)));
        var partes = new[] { rua, q.Bairro, q.Cidade, q.Uf, q.Cep }
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s!.Trim());
        return string.Join(" - ", partes);
    }

    private sealed record GoogleResposta
    {
        [JsonPropertyName("status")] public string? Status { get; init; }
        [JsonPropertyName("results")] public List<GoogleResultado>? Results { get; init; }
    }

    private sealed record GoogleResultado
    {
        [JsonPropertyName("geometry")] public GoogleGeometria? Geometry { get; init; }
        [JsonPropertyName("address_components")] public List<GoogleComponente>? AddressComponents { get; init; }
    }

    private sealed record GoogleGeometria
    {
        [JsonPropertyName("location")] public GoogleLocal? Location { get; init; }
        [JsonPropertyName("location_type")] public string? LocationType { get; init; }
    }

    private sealed record GoogleLocal
    {
        [JsonPropertyName("lat")] public double Lat { get; init; }
        [JsonPropertyName("lng")] public double Lng { get; init; }
    }

    private sealed record GoogleComponente
    {
        [JsonPropertyName("types")] public List<string>? Types { get; init; }
    }
}
