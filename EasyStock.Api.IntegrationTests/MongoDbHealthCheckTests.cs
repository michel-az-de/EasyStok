using DotNet.Testcontainers.Builders;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Testcontainers.MongoDb;

namespace EasyStock.Api.IntegrationTests;

public class MongoDbHealthCheckTests : IAsyncLifetime
{
    private MongoDbContainer? _container;
    private bool _isAvailable;

    public async Task InitializeAsync()
    {
        try
        {
            _container = new MongoDbBuilder("mongo:7.0")
                .Build();

            await _container.StartAsync();
            _isAvailable = true;
        }
        catch (DockerUnavailableException)
        {
            _isAvailable = false;
        }
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
            await _container.DisposeAsync();
    }

    [SkippableFact]
    public async Task Health_deve_subir_com_provider_mongo()
    {
        Skip.If(!_isAvailable || _container is null, "Docker/MongoDB unavailable");

        var databaseName = $"easystock_api_tests_{Guid.NewGuid():N}";

        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((_, config) =>
                {
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Database:Provider"] = "MongoDb",
                        ["Database:MongoDatabase"] = databaseName,
                        ["ConnectionStrings:MongoConnection"] = _container.GetConnectionString(),
                        ["Anthropic:Enabled"] = "false",
                        ["Jwt:Issuer"] = "EasyStock",
                        ["Jwt:Audience"] = "EasyStock",
                        ["Jwt:SecretKey"] = "EasyStock-SuperSecretKey-Min32Chars!!"
                    });
                });
            });

        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        var response = await client.GetAsync("/health");

        response.EnsureSuccessStatusCode();
    }
}
