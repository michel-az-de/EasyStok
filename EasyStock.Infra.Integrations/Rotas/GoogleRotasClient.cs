using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using EasyStock.Application.Ports.Output.Lookup;
using Microsoft.Extensions.Logging;

namespace EasyStock.Infra.Integrations.Rotas;

/// <summary>
/// Adapter para a <see href="https://developers.google.com/maps/documentation/routes">Google
/// Routes API</see> (<c>directions/v2:computeRoutes</c>), sucessora da Distance Matrix,
/// que não é mais habilitável em projeto novo (issue #1274).
///
/// <para>
/// <strong>Contrato</strong>: nunca lança. Sem rota, timeout, 4xx/5xx ou JSON inválido
/// viram <see langword="null"/>. A chave vai no header <c>X-Goog-Api-Key</c>, nunca na URL.
/// O <c>X-Goog-FieldMask</c> pede só distância e duração (a Routes API cobra por campo).
/// </para>
/// </summary>
public sealed class GoogleRotasClient : IRotaClient
{
    private const string FieldMask = "routes.distanceMeters,routes.duration";

    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly ILogger<GoogleRotasClient> _logger;

    public GoogleRotasClient(HttpClient http, string apiKey, ILogger<GoogleRotasClient> logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        _http = http;
        _apiKey = apiKey;
        _logger = logger;
    }

    public async Task<RotaResultado?> MedirAsync(RotaQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var corpo = new
        {
            origin = Ponto(query.OrigemLat, query.OrigemLng),
            destination = Ponto(query.DestinoLat, query.DestinoLng),
            travelMode = "DRIVE",
            routingPreference = "TRAFFIC_UNAWARE",
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, "directions/v2:computeRoutes")
        {
            Content = JsonContent.Create(corpo),
        };
        req.Headers.Add("X-Goog-Api-Key", _apiKey);
        req.Headers.Add("X-Goog-FieldMask", FieldMask);

        try
        {
            using var resp = await _http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode)
            {
                // 403/429 indicam chave ou cota, não endereço ruim.
                if ((int)resp.StatusCode is 403 or 429)
                    _logger.LogWarning("Google Routes retornou HTTP {Status}", (int)resp.StatusCode);
                else
                    _logger.LogDebug("Google Routes retornou HTTP {Status}", (int)resp.StatusCode);
                return null;
            }

            var resposta = await resp.Content.ReadFromJsonAsync<RoutesResposta>(ct);
            var rota = resposta?.Routes is { Count: > 0 } ? resposta.Routes[0] : null;
            if (rota?.DistanceMeters is not { } metros) return null;

            var segundos = ParseDuracao(rota.Duration);
            if (segundos is null) return null;

            return new RotaResultado(metros, segundos.Value);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger.LogDebug("Google Routes timeout");
            return null;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogDebug("Google Routes HTTP falhou: {Status}", ex.StatusCode);
            return null;
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException or NotSupportedException)
        {
            _logger.LogDebug("Google Routes JSON inválido");
            return null;
        }
    }

    private static object Ponto(double lat, double lng) =>
        new { location = new { latLng = new { latitude = lat, longitude = lng } } };

    /// <summary>A Routes API devolve a duração como string protobuf: <c>"612s"</c>.</summary>
    private static int? ParseDuracao(string? duracao)
    {
        if (string.IsNullOrWhiteSpace(duracao) || !duracao.EndsWith('s')) return null;
        return double.TryParse(duracao[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var s)
            ? (int)Math.Round(s, MidpointRounding.AwayFromZero)
            : null;
    }

    private sealed record RoutesResposta
    {
        [JsonPropertyName("routes")] public List<Rota>? Routes { get; init; }
    }

    private sealed record Rota
    {
        [JsonPropertyName("distanceMeters")] public int? DistanceMeters { get; init; }
        [JsonPropertyName("duration")] public string? Duration { get; init; }
    }
}
