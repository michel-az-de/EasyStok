using System.Net.Http.Json;
using System.Text.Json.Serialization;
using EasyStock.Application.Ports.Output.Lookup;
using Microsoft.Extensions.Logging;

namespace EasyStock.Infra.Integrations.TomTom;

/// <summary>
/// Adapter para a <see href="https://developer.tomtom.com/geocoding-api/documentation/geocode">TomTom
/// Geocoding API</see>: endereço → coordenada (issue #1322).
///
/// <para>
/// <strong>Contrato</strong>: nunca lança. Sem resultado, timeout, 4xx/5xx ou JSON
/// inválido viram <see langword="null"/>. A TomTom só aceita a chave no parâmetro
/// <c>key</c> da URL, então a URL NUNCA é logada.
/// </para>
///
/// <para>
/// <strong>Confiança</strong> (ADR-0017): só é confiável com <c>type</c>
/// <c>Point Address</c> ou <c>Address Range</c> E <c>address.streetNumber</c> presente.
/// </para>
/// </summary>
public sealed class TomTomGeocodingClient : IGeocodingClient
{
    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly ILogger<TomTomGeocodingClient> _logger;

    public TomTomGeocodingClient(HttpClient http, string apiKey, ILogger<TomTomGeocodingClient> logger)
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

        var url = $"search/2/geocode/{Uri.EscapeDataString(endereco)}.json?"
                  + $"countrySet=BR&limit=1&language=pt-BR&key={Uri.EscapeDataString(_apiKey)}";

        try
        {
            using var resp = await _http.GetAsync(url, ct);
            if (!resp.IsSuccessStatusCode)
            {
                TomTomLog.Http(_logger, "Geocoding", (int)resp.StatusCode);
                return null;
            }

            var corpo = await resp.Content.ReadFromJsonAsync<TomTomGeocodeResposta>(ct);
            var primeiro = corpo?.Results is { Count: > 0 } ? corpo.Results[0] : null;
            if (primeiro?.Position is not { } pos) return null;

            var granular = primeiro.Type is "Point Address" or "Address Range";
            var temNumero = !string.IsNullOrWhiteSpace(primeiro.Address?.StreetNumber);

            return new GeocodeResultado(pos.Lat, pos.Lon, granular && temNumero);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger.LogDebug("TomTom Geocoding timeout");
            return null;
        }
        catch (HttpRequestException ex)
        {
            // Não loga a exceção inteira: a mensagem pode carregar a URL com a chave.
            _logger.LogDebug("TomTom Geocoding HTTP falhou: {Status}", ex.StatusCode);
            return null;
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException or NotSupportedException)
        {
            _logger.LogDebug("TomTom Geocoding JSON inválido");
            return null;
        }
    }

    /// <summary>Endereço em linha única no formato brasileiro, ou null sem logradouro, cidade nem CEP.</summary>
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

    private sealed record TomTomGeocodeResposta
    {
        [JsonPropertyName("results")] public List<TomTomResultado>? Results { get; init; }
    }

    private sealed record TomTomResultado
    {
        [JsonPropertyName("type")] public string? Type { get; init; }
        [JsonPropertyName("address")] public TomTomEndereco? Address { get; init; }
        [JsonPropertyName("position")] public TomTomPosicao? Position { get; init; }
    }

    private sealed record TomTomEndereco
    {
        [JsonPropertyName("streetNumber")] public string? StreetNumber { get; init; }
    }

    private sealed record TomTomPosicao
    {
        [JsonPropertyName("lat")] public double Lat { get; init; }
        [JsonPropertyName("lon")] public double Lon { get; init; }
    }
}
