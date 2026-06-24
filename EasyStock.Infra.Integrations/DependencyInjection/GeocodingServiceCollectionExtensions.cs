using EasyStock.Application.Ports.Output.Lookup;
using EasyStock.Infra.Integrations.Geocoding;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

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

    public static IServiceCollection AddEasyStockGeocoding(
        this IServiceCollection services,
        IConfiguration configuration)
    {
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
