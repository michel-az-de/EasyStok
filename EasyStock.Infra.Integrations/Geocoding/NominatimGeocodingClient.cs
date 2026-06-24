using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using EasyStock.Application.Ports.Output.Lookup;
using Microsoft.Extensions.Logging;

namespace EasyStock.Infra.Integrations.Geocoding;

/// <summary>
/// Adapter HTTP para o <see href="https://nominatim.org/">Nominatim</see> (OpenStreetMap) —
/// geocoding de endereço → coordenada. Funciona contra o Nominatim público OU uma
/// instância self-host (mesma API); a base URL vem da config (ADR-0017/0023).
///
/// <para>
/// <strong>Contrato</strong>: nunca lança. Sem match, timeout, 4xx/5xx ou JSON
/// inválido viram <see langword="null"/> (best-effort). O use case de frete cai
/// para a tabela de zonas quando vem null.
/// </para>
///
/// <para>
/// <strong>Confiança</strong> (ADR-0017): só é confiável com granularidade de
/// número — <c>addresstype == "house"</c> E <c>address.house_number</c> presente.
/// Caso contrário (rua/bairro/cidade) o resultado vem com <c>Confiavel = false</c>.
/// </para>
/// </summary>
public sealed class NominatimGeocodingClient : IGeocodingClient
{
    private readonly HttpClient _http;
    private readonly ILogger<NominatimGeocodingClient> _logger;

    public NominatimGeocodingClient(HttpClient http, ILogger<NominatimGeocodingClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<GeocodeResultado?> GeocodificarAsync(GeocodeQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var url = MontarUrl(query);
        if (url is null) return null; // sem componente de endereço útil → não bate na rede

        try
        {
            using var resp = await _http.GetAsync(url, ct);
            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogDebug("Nominatim retornou {Status}", (int)resp.StatusCode);
                return null;
            }

            var resultados = await resp.Content.ReadFromJsonAsync<List<NominatimResultado>>(ct);
            var primeiro = resultados is { Count: > 0 } ? resultados[0] : null;
            if (primeiro is null) return null;

            if (!double.TryParse(primeiro.Lat, NumberStyles.Float, CultureInfo.InvariantCulture, out var lat)
                || !double.TryParse(primeiro.Lon, NumberStyles.Float, CultureInfo.InvariantCulture, out var lng))
                return null;

            var confiavel = string.Equals(primeiro.AddressType, "house", StringComparison.OrdinalIgnoreCase)
                            && !string.IsNullOrWhiteSpace(primeiro.Address?.HouseNumber);

            return new GeocodeResultado(lat, lng, confiavel);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger.LogDebug("Nominatim timeout");
            return null;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogDebug(ex, "Nominatim HTTP falhou");
            return null;
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException)
        {
            _logger.LogDebug(ex, "Nominatim JSON inválido");
            return null;
        }
    }

    /// <summary>
    /// Monta a busca estruturada do Nominatim. <see langword="null"/> se não há
    /// nenhum componente de endereço útil (não desperdiça chamada).
    /// </summary>
    private static string? MontarUrl(GeocodeQuery q)
    {
        var street = string.Join(' ', new[] { q.Numero, q.Logradouro }
            .Where(s => !string.IsNullOrWhiteSpace(s))).Trim();

        var temEndereco = !string.IsNullOrWhiteSpace(street)
                          || !string.IsNullOrWhiteSpace(q.Cidade)
                          || !string.IsNullOrWhiteSpace(q.Cep);
        if (!temEndereco) return null;

        var parts = new List<string>
        {
            "format=jsonv2",
            "addressdetails=1",
            "limit=1",
            "countrycodes=br",
        };

        void Add(string key, string? val)
        {
            if (!string.IsNullOrWhiteSpace(val))
                parts.Add($"{key}={Uri.EscapeDataString(val.Trim())}");
        }

        Add("street", string.IsNullOrWhiteSpace(street) ? null : street);
        Add("city", q.Cidade);
        Add("state", q.Uf);
        Add("postalcode", q.Cep);

        return $"search?{string.Join('&', parts)}";
    }

    private sealed record NominatimResultado
    {
        [JsonPropertyName("lat")] public string? Lat { get; init; }
        [JsonPropertyName("lon")] public string? Lon { get; init; }
        [JsonPropertyName("addresstype")] public string? AddressType { get; init; }
        [JsonPropertyName("address")] public NominatimAddress? Address { get; init; }
    }

    private sealed record NominatimAddress
    {
        [JsonPropertyName("house_number")] public string? HouseNumber { get; init; }
    }
}
