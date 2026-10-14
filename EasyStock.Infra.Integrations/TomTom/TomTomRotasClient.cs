using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using EasyStock.Application.Ports.Output.Lookup;
using Microsoft.Extensions.Logging;

namespace EasyStock.Infra.Integrations.TomTom;

/// <summary>
/// Adapter para a <see href="https://developer.tomtom.com/routing-api/documentation/calculate-route">TomTom
/// Routing API</see>: distância e duração reais de carro (issue #1322).
///
/// <para>
/// <strong>Contrato</strong>: nunca lança. Sem rota, timeout, 4xx/5xx ou JSON inválido
/// viram <see langword="null"/>. Chave na URL (exigência da TomTom), então a URL nunca é logada.
/// Sem trânsito (<c>traffic=false</c>): o frete não pode oscilar com a hora do pedido.
/// </para>
/// </summary>
public sealed class TomTomRotasClient : IRotaClient
{
    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly ILogger<TomTomRotasClient> _logger;

    public TomTomRotasClient(HttpClient http, string apiKey, ILogger<TomTomRotasClient> logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        _http = http;
        _apiKey = apiKey;
        _logger = logger;
    }

    public async Task<RotaResultado?> MedirAsync(RotaQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var trecho = $"{Ponto(query.OrigemLat, query.OrigemLng)}:{Ponto(query.DestinoLat, query.DestinoLng)}";
        var url = $"routing/1/calculateRoute/{trecho}/json?travelMode=car&traffic=false"
                  + $"&key={Uri.EscapeDataString(_apiKey)}";

        try
        {
            using var resp = await _http.GetAsync(url, ct);
            if (!resp.IsSuccessStatusCode)
            {
                TomTomLog.Http(_logger, "Routing", (int)resp.StatusCode);
                return null;
            }

            var corpo = await resp.Content.ReadFromJsonAsync<TomTomRotaResposta>(ct);
            var resumo = corpo?.Routes is { Count: > 0 } ? corpo.Routes[0].Summary : null;
            if (resumo?.LengthInMeters is not { } metros || resumo.TravelTimeInSeconds is not { } segundos)
                return null;

            return new RotaResultado(metros, segundos);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger.LogDebug("TomTom Routing timeout");
            return null;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogDebug("TomTom Routing HTTP falhou: {Status}", ex.StatusCode);
            return null;
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException or NotSupportedException)
        {
            _logger.LogDebug("TomTom Routing JSON inválido");
            return null;
        }
    }

    private static string Ponto(double lat, double lng) =>
        string.Create(CultureInfo.InvariantCulture, $"{lat},{lng}");

    private sealed record TomTomRotaResposta
    {
        [JsonPropertyName("routes")] public List<TomTomRota>? Routes { get; init; }
    }

    private sealed record TomTomRota
    {
        [JsonPropertyName("summary")] public TomTomResumo? Summary { get; init; }
    }

    private sealed record TomTomResumo
    {
        [JsonPropertyName("lengthInMeters")] public int? LengthInMeters { get; init; }
        [JsonPropertyName("travelTimeInSeconds")] public int? TravelTimeInSeconds { get; init; }
    }
}
