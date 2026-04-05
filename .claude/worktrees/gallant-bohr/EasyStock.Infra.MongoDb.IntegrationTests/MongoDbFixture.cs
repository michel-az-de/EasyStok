using DotNet.Testcontainers.Builders;
using EasyStock.Application.UseCases.AutenticarUsuario;
using EasyStock.Application.UseCases.RegistrarEntradaEstoque;
using EasyStock.Application.UseCases.RegistrarSaidaEstoque;
using EasyStock.Application.UseCases.ReporEstoque;
using EasyStock.Infra.MongoDb.Data;
using EasyStock.Infra.MongoDb.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.MongoDb;

namespace EasyStock.Infra.MongoDb.IntegrationTests;

public sealed class MongoDbFixture : IAsyncLifetime
{
    private MongoDbContainer? _container;
    private readonly string _databaseName = $"easystock_tests_{Guid.NewGuid():N}";

    public bool IsAvailable { get; private set; }
    public string? UnavailableReason { get; private set; }

    public async Task InitializeAsync()
    {
        try
        {
            _container = new MongoDbBuilder("mongo:7.0")
                .Build();

            await _container.StartAsync();
            IsAvailable = true;
            await ResetDatabaseAsync();
        }
        catch (DockerUnavailableException ex)
        {
            IsAvailable = false;
            UnavailableReason = ex.Message;
        }
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
            await _container.DisposeAsync();
    }

    public ServiceProvider CreateServiceProvider()
    {
        if (_container is null)
            throw new InvalidOperationException("MongoDB de teste indisponivel.");

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Anthropic:Enabled"] = "false",
                ["Database:MongoDatabase"] = _databaseName
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddEasyStockMongoInfrastructure(_container.GetConnectionString(), _databaseName, configuration);
        services.AddScoped<RegistrarEntradaEstoqueUseCase>();
        services.AddScoped<RegistrarSaidaEstoqueUseCase>();
        services.AddScoped<ReporEstoqueUseCase>();
        services.AddScoped<AutenticarUsuarioUseCase>();

        return services.BuildServiceProvider();
    }

    public async Task ResetDatabaseAsync()
    {
        if (!IsAvailable || _container is null)
            return;

        await using var provider = CreateServiceProvider();
        using var scope = provider.CreateScope();

        var client = scope.ServiceProvider.GetRequiredService<MongoDB.Driver.IMongoClient>();
        await client.DropDatabaseAsync(_databaseName);

        var runner = scope.ServiceProvider.GetRequiredService<MongoMigrationRunner>();
        await runner.ApplyAsync();
    }
}
