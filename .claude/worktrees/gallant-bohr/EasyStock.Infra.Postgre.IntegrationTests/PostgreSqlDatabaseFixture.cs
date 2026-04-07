using EasyStock.Infra.Postgre.Data;
using DotNet.Testcontainers.Builders;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace EasyStock.Infra.Postgre.IntegrationTests;

public sealed class PostgreSqlDatabaseFixture : IAsyncLifetime
{
    private PostgreSqlContainer? _container;
    public bool IsAvailable { get; private set; }
    public string? UnavailableReason { get; private set; }

    public async Task InitializeAsync()
    {
        try
        {
            _container = new PostgreSqlBuilder("postgres:16-alpine")
                .WithDatabase("easystock_tests")
                .WithUsername("postgres")
                .WithPassword("postgres")
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

    public EasyStockDbContext CreateDbContext()
    {
        if (_container is null) throw new InvalidOperationException("PostgreSQL de teste indisponivel.");

        var options = new DbContextOptionsBuilder<EasyStockDbContext>()
            .UseNpgsql(_container.GetConnectionString())
            .Options;

        return new EasyStockDbContext(options);
    }

    public async Task ResetDatabaseAsync()
    {
        if (!IsAvailable || _container is null) return;

        await using var context = CreateDbContext();
        await context.Database.EnsureDeletedAsync();
        await context.Database.MigrateAsync();
    }
}
