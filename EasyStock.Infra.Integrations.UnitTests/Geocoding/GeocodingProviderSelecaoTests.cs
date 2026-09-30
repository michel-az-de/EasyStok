using EasyStock.Application.Ports.Output.Lookup;
using EasyStock.Infra.Integrations.DependencyInjection;
using EasyStock.Infra.Integrations.Geocoding;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyStock.Infra.Integrations.UnitTests.Geocoding;

/// <summary>Seleção do provedor de geocoding por configuração (issue #1217).</summary>
public class GeocodingProviderSelecaoTests
{
    private static IGeocodingClient Resolver(Dictionary<string, string?> config)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(config).Build();
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddEasyStockGeocoding(configuration);
        return services.BuildServiceProvider().CreateScope().ServiceProvider.GetRequiredService<IGeocodingClient>();
    }

    [Fact]
    public void Google_com_chave_usa_adapter_google()
    {
        var client = Resolver(new()
        {
            [GeocodingServiceCollectionExtensions.ProviderKey] = "google",
            [GeocodingServiceCollectionExtensions.GoogleApiKeyKey] = "k",
        });

        Assert.IsType<GoogleGeocodingClient>(client);
    }

    [Fact]
    public void Google_sem_chave_cai_em_noop()
    {
        var client = Resolver(new()
        {
            [GeocodingServiceCollectionExtensions.ProviderKey] = "google",
        });

        Assert.IsType<NoOpGeocodingClient>(client);
    }

    [Fact]
    public void Sem_provider_mantem_comportamento_do_nominatim_desligado()
    {
        var client = Resolver(new()
        {
            [GeocodingServiceCollectionExtensions.FeatureFlagKey] = "false",
        });

        Assert.IsType<NoOpGeocodingClient>(client);
    }
}
