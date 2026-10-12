using EasyStock.Application.Ports.Output.Lookup;
using EasyStock.Infra.Integrations.Geocoding;
using EasyStock.Infra.Integrations.Rotas;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EasyStock.Infra.Integrations.DependencyInjection;

/// <summary>
/// Registro do adapter <see cref="IGeocodingClient"/> (frete por raio, ADR-0017).
///
/// <para>
/// Feature flag <c>ENABLE_NOMINATIM_GEOCODING</c> (default: <see langword="false"/>):
/// </para>
/// <list type="bullet">
///   <item><c>true</c> → <see cref="NominatimGeocodingClient"/> com <c>HttpClient</c> (timeout 2s, User-Agent do ToS).</item>
///   <item><c>false</c> → <see cref="NoOpGeocodingClient"/> (não bate na rede; frete cai pra zona).</item>
/// </list>
///
/// <para>
/// Default desligado em dev/CI. Production liga via env var
/// <c>ENABLE_NOMINATIM_GEOCODING=true</c> quando o serviço (público ou self-host)
/// estiver disponível. A base URL é configurável (<c>Storefront:Frete:NominatimBaseUrl</c>)
/// para apontar pro container self-host quando ele subir.
/// </para>
/// </summary>
public static class GeocodingServiceCollectionExtensions
{
    /// <summary>Caminho da flag em <see cref="IConfiguration"/>.</summary>
    public const string FeatureFlagKey = "Storefront:Frete:EnableNominatimGeocoding";

    /// <summary>Env var alternativa (Docker/fly).</summary>
    public const string FeatureFlagEnvVar = "ENABLE_NOMINATIM_GEOCODING";

    /// <summary>Base URL default (Nominatim público). Override pra apontar pro self-host.</summary>
    public const string NominatimDefaultBaseUrl = "https://nominatim.openstreetmap.org/";

    /// <summary>Provedor explícito (<c>google</c>). Ausente = comportamento Nominatim acima (issue #1217).</summary>
    public const string ProviderKey = "Storefront:Frete:GeocodingProvider";

    /// <summary>Chave da Google Maps Platform. Env var alternativa: <see cref="GoogleApiKeyEnvVar"/>.</summary>
    public const string GoogleApiKeyKey = "Storefront:Frete:GoogleMapsApiKey";

    public const string GoogleApiKeyEnvVar = "GOOGLE_MAPS_API_KEY";

    public const string GoogleDefaultBaseUrl = "https://maps.googleapis.com/maps/api/";

    public const string GoogleRoutesBaseUrl = "https://routes.googleapis.com/";

    public static IServiceCollection AddEasyStockGeocoding(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var google = string.Equals(configuration[ProviderKey], "google", StringComparison.OrdinalIgnoreCase);
        var googleKey = google ? ResolveGoogleKey(configuration) : null;

        AddRotas(services, googleKey);

        if (google)
            return AddGoogle(services, googleKey);

        var enabled = ResolveEnabled(configuration);
        if (!enabled)
        {
            services.AddScoped<IGeocodingClient, NoOpGeocodingClient>();
            return services;
        }

        var baseUrl = configuration["Storefront:Frete:NominatimBaseUrl"] ?? NominatimDefaultBaseUrl;

        services.AddHttpClient<IGeocodingClient, NominatimGeocodingClient>(client =>
        {
            client.BaseAddress = new Uri(baseUrl);
            client.Timeout = TimeSpan.FromSeconds(2);
            // ToS do Nominatim exige User-Agent identificável.
            client.DefaultRequestHeaders.UserAgent.ParseAdd("EasyStok-Storefront/1.0 (+contato@casadababa.app)");
        });

        return services;
    }

    /// <summary>
    /// Google sem chave cai em NoOp: o frete continua pela zona e nada bate na rede.
    /// </summary>
    private static IServiceCollection AddGoogle(IServiceCollection services, string? apiKey)
    {
        if (apiKey is null)
        {
            services.AddScoped<IGeocodingClient, NoOpGeocodingClient>();
            return services;
        }

        services.AddHttpClient(nameof(GoogleGeocodingClient), client =>
        {
            client.BaseAddress = new Uri(GoogleDefaultBaseUrl);
            client.Timeout = TimeSpan.FromSeconds(2);
        });
        services.AddScoped<IGeocodingClient>(sp => new GoogleGeocodingClient(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(GoogleGeocodingClient)),
            apiKey,
            sp.GetRequiredService<ILogger<GoogleGeocodingClient>>()));

        return services;
    }

    /// <summary>
    /// Rota real (Routes API, issue #1274) usa a mesma chave do geocoding Google.
    /// Sem Google completo, NoOp: o frete segue com <c>haversine × FatorRota</c>.
    /// </summary>
    private static void AddRotas(IServiceCollection services, string? googleKey)
    {
        if (googleKey is null)
        {
            services.AddScoped<IRotaClient, NoOpRotaClient>();
            return;
        }

        services.AddHttpClient(nameof(GoogleRotasClient), client =>
        {
            client.BaseAddress = new Uri(GoogleRoutesBaseUrl);
            client.Timeout = TimeSpan.FromSeconds(2);
        });
        services.AddScoped<IRotaClient>(sp => new GoogleRotasClient(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(GoogleRotasClient)),
            googleKey,
            sp.GetRequiredService<ILogger<GoogleRotasClient>>()));
    }

    private static string? ResolveGoogleKey(IConfiguration configuration)
    {
        var apiKey = configuration[GoogleApiKeyKey];
        if (string.IsNullOrWhiteSpace(apiKey))
            apiKey = Environment.GetEnvironmentVariable(GoogleApiKeyEnvVar);
        return string.IsNullOrWhiteSpace(apiKey) ? null : apiKey;
    }

    private static bool ResolveEnabled(IConfiguration configuration)
    {
        var fromConfig = configuration[FeatureFlagKey];
        if (TryParseBool(fromConfig, out var configBool))
            return configBool;

        var fromEnv = Environment.GetEnvironmentVariable(FeatureFlagEnvVar);
        if (TryParseBool(fromEnv, out var envBool))
            return envBool;

        return false; // default: desligado
    }

    private static bool TryParseBool(string? value, out bool parsed)
    {
        parsed = false;
        if (string.IsNullOrWhiteSpace(value)) return false;
        if (bool.TryParse(value, out parsed)) return true;
        if (value == "1") { parsed = true; return true; }
        if (value == "0") { parsed = false; return true; }
        return false;
    }
}
