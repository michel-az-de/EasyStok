using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace EasyStock.Infra.Postgre.Data;

/// <summary>
/// Factory usado pelo EF Core em design-time (ex.: ao rodar
/// <c>dotnet ef migrations add</c>). Exige connection string vinda
/// de <c>appsettings.json</c> ou variável de ambiente
/// <c>ConnectionStrings__DefaultConnection</c>; não há fallback
/// hardcoded para evitar vazamento de credenciais em build logs.
/// </summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<EasyStockDbContext>
{
    public EasyStockDbContext CreateDbContext(string[] args)
    {
        var builder = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddEnvironmentVariables();

        var configuration = builder.Build();

        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException(
                "Connection string 'DefaultConnection' não encontrada. " +
                "Configure via appsettings.json ou variável de ambiente " +
                "ConnectionStrings__DefaultConnection antes de rodar comandos EF.");

        var optionsBuilder = new DbContextOptionsBuilder<EasyStockDbContext>();
        optionsBuilder.UseNpgsql(connectionString);
        optionsBuilder.ConfigureWarnings(w =>
            w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning));

        return new EasyStockDbContext(optionsBuilder.Options);
    }
}
