using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace EasyStock.Infra.Sqlite.Startup;

public sealed class SqliteInitializerHostedService(
    IServiceScopeFactory scopeFactory,
    ILogger<SqliteInitializerHostedService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();

        logger.LogInformation("Inicializando banco de dados SQLite...");
        await context.Database.EnsureCreatedAsync(cancellationToken);
        logger.LogInformation("Banco de dados SQLite inicializado com sucesso.");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
