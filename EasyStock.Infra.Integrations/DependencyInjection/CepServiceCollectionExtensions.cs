using EasyStock.Application.Ports.Output.Lookup;
using EasyStock.Infra.Integrations.Cep;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EasyStock.Infra.Integrations.DependencyInjection;

/// <summary>
/// Registro do adapter <see cref="ICepLookupClient"/>.
///
/// <para>
/// Feature flag <c>ENABLE_VIACEP_LOOKUP</c> (default: <see langword="false"/>):
/// </para>
/// <list type="bullet">
///   <item><c>true</c> → <see cref="ViaCepLookupClient"/> com <c>HttpClient</c> (timeout 1s).</item>
///   <item><c>false</c> → <see cref="NoOpCepLookupClient"/> (não bate na API externa).</item>
/// </list>
///
/// <para>
/// Em dev/CI o default é <c>NoOp</c> para não depender de internet nem inflar
/// latência de testes. Production deve ligar via env var
/// <c>ENABLE_VIACEP_LOOKUP=true</c>.
/// </para>
/// </summary>
public static class CepServiceCollectionExtensions
{
    /// <summary>Caminho hierárquico da flag em <see cref="IConfiguration"/>.</summary>
    public const string FeatureFlagKey = "Storefront:Frete:EnableViaCepLookup";

    /// <summary>Env var alternativa (mais conveniente em Docker/fly).</summary>
    public const string FeatureFlagEnvVar = "ENABLE_VIACEP_LOOKUP";

    /// <summary>URL base do ViaCEP. Override via config <c>Storefront:Frete:ViaCepBaseUrl</c>.</summary>
    public const string ViaCepDefaultBaseUrl = "https://viacep.com.br/";

    public static IServiceCollection AddEasyStockCepLookup(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var enabled = ResolveEnabled(configuration);
        if (!enabled)
        {
            services.AddScoped<ICepLookupClient, NoOpCepLookupClient>();
            return services;
        }

        var baseUrl = configuration["Storefront:Frete:ViaCepBaseUrl"] ?? ViaCepDefaultBaseUrl;

        services.AddHttpClient<ICepLookupClient, ViaCepLookupClient>(client =>
        {
            client.BaseAddress = new Uri(baseUrl);
            client.Timeout = TimeSpan.FromSeconds(1);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("EasyStok-Storefront/1.0 (+contato@casadababa.app)");
        });

        return services;
    }

    private static bool ResolveEnabled(IConfiguration configuration)
    {
        // 1. Config hierárquica (appsettings.json)
        var fromConfig = configuration[FeatureFlagKey];
        if (TryParseBool(fromConfig, out var configBool))
            return configBool;

        // 2. Env var (Docker/fly)
        var fromEnv = Environment.GetEnvironmentVariable(FeatureFlagEnvVar);
        if (TryParseBool(fromEnv, out var envBool))
            return envBool;

        return false; // Default: desligado
    }

    private static bool TryParseBool(string? value, out bool parsed)
    {
        parsed = false;
        if (string.IsNullOrWhiteSpace(value)) return false;
        if (bool.TryParse(value, out parsed)) return true;
        // Aceita "1"/"0" também
        if (value == "1") { parsed = true; return true; }
        if (value == "0") { parsed = false; return true; }
        return false;
    }
}
