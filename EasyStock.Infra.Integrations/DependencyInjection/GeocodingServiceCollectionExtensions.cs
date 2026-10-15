using EasyStock.Application.Ports.Output.Lookup;
using EasyStock.Infra.Integrations.Geocoding;
using EasyStock.Infra.Integrations.Rotas;
using EasyStock.Infra.Integrations.TomTom;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EasyStock.Infra.Integrations.DependencyInjection;

/// <summary>
/// Registro do adapter <see cref="IGeocodingClient"/> (frete por raio, ADR-0017).
///
/// <para>
/// Os provedores formam uma cadeia (I8.1): o de <c>Storefront:Frete:GeocodingProvider</c> com chave
/// (<c>tomtom</c> ou <c>google</c>) vem primeiro e o Nominatim, se ligado, depois. Falha ou resultado
/// não confiável de um cai no seguinte (<see cref="CadeiaGeocodingClient"/>). Sem nenhum, NoOp.
/// </para>
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

    /// <summary>Provedor explícito (<c>google</c> ou <c>tomtom</c>). Ausente = comportamento Nominatim acima (issues #1217, #1322).</summary>
    public const string ProviderKey = "Storefront:Frete:GeocodingProvider";

    /// <summary>Chave da Google Maps Platform. Env var alternativa: <see cref="GoogleApiKeyEnvVar"/>.</summary>
    public const string GoogleApiKeyKey = "Storefront:Frete:GoogleMapsApiKey";

    public const string GoogleApiKeyEnvVar = "GOOGLE_MAPS_API_KEY";

    public const string GoogleDefaultBaseUrl = "https://maps.googleapis.com/maps/api/";

    public const string GoogleRoutesBaseUrl = "https://routes.googleapis.com/";

    /// <summary>Chave da TomTom (plano gratuito, sem cartão — issue #1322). Env var: <see cref="TomTomApiKeyEnvVar"/>.</summary>
    public const string TomTomApiKeyKey = "Storefront:Frete:TomTomApiKey";

    public const string TomTomApiKeyEnvVar = "TOMTOM_API_KEY";

    public const string TomTomBaseUrl = "https://api.tomtom.com/";

    public static IServiceCollection AddEasyStockGeocoding(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Cadeia (I8.1, #1658): provedor com chave, depois Nominatim. Um elo só é registrado direto;
        // nenhum é NoOp (frete por zona, nada bate na rede).
        var elos = new List<Func<IServiceProvider, IGeocodingClient>>();

        var provider = configuration[ProviderKey];
        var tomTomKey = string.Equals(provider, "tomtom", StringComparison.OrdinalIgnoreCase)
            ? ResolveKey(configuration, TomTomApiKeyKey, TomTomApiKeyEnvVar)
            : null;
        var googleKey = string.Equals(provider, "google", StringComparison.OrdinalIgnoreCase)
            ? ResolveGoogleKey(configuration)
            : null;

        if (tomTomKey is not null)
            elos.Add(AddTomTom(services, tomTomKey));
        else
            AddRotas(services, googleKey);

        if (googleKey is not null)
            elos.Add(AddGoogle(services, googleKey));

        if (ResolveEnabled(configuration))
            elos.Add(AddNominatim(services, configuration));

        switch (elos.Count)
        {
            case 0:
                services.AddScoped<IGeocodingClient, NoOpGeocodingClient>();
                break;
            case 1:
                services.AddScoped(elos[0]);
                break;
            default:
                services.AddScoped<IGeocodingClient>(sp => new CadeiaGeocodingClient(
                    [.. elos.Select(criar => criar(sp))],
                    sp.GetRequiredService<ILogger<CadeiaGeocodingClient>>()));
                break;
        }

        return services;
    }

    private static Func<IServiceProvider, IGeocodingClient> AddNominatim(IServiceCollection services, IConfiguration configuration)
    {
        var baseUrl = configuration["Storefront:Frete:NominatimBaseUrl"] ?? NominatimDefaultBaseUrl;

        services.AddHttpClient(nameof(NominatimGeocodingClient), client =>
        {
            client.BaseAddress = new Uri(baseUrl);
            client.Timeout = TimeSpan.FromSeconds(2);
            // ToS do Nominatim exige User-Agent identificável.
            client.DefaultRequestHeaders.UserAgent.ParseAdd("EasyStok-Storefront/1.0 (+contato@casadababa.app)");
        });

        return sp => new NominatimGeocodingClient(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(NominatimGeocodingClient)),
            sp.GetRequiredService<ILogger<NominatimGeocodingClient>>());
    }

    private static Func<IServiceProvider, IGeocodingClient> AddGoogle(IServiceCollection services, string apiKey)
    {
        services.AddHttpClient(nameof(GoogleGeocodingClient), client =>
        {
            client.BaseAddress = new Uri(GoogleDefaultBaseUrl);
            client.Timeout = TimeSpan.FromSeconds(2);
        });

        return sp => new GoogleGeocodingClient(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(GoogleGeocodingClient)),
            apiKey,
            sp.GetRequiredService<ILogger<GoogleGeocodingClient>>());
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

    /// <summary>TomTom: geocoding e rota de carro com a mesma chave (issue #1322).</summary>
    private static Func<IServiceProvider, IGeocodingClient> AddTomTom(IServiceCollection services, string apiKey)
    {
        services.AddHttpClient(nameof(TomTomBaseUrl), client =>
        {
            client.BaseAddress = new Uri(TomTomBaseUrl);
            client.Timeout = TimeSpan.FromSeconds(2);
        });
        services.AddScoped<IRotaClient>(sp => new TomTomRotasClient(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(TomTomBaseUrl)),
            apiKey,
            sp.GetRequiredService<ILogger<TomTomRotasClient>>()));

        return sp => new TomTomGeocodingClient(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(TomTomBaseUrl)),
            apiKey,
            sp.GetRequiredService<ILogger<TomTomGeocodingClient>>());
    }

    private static string? ResolveGoogleKey(IConfiguration configuration) =>
        ResolveKey(configuration, GoogleApiKeyKey, GoogleApiKeyEnvVar);

    private static string? ResolveKey(IConfiguration configuration, string configKey, string envVar)
    {
        var apiKey = configuration[configKey];
        if (string.IsNullOrWhiteSpace(apiKey))
            apiKey = Environment.GetEnvironmentVariable(envVar);
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
