using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.Configuration.EnvironmentVariables;
using System.IO;

namespace EasyStock.Infra.Postgre.Data
{
    public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<EasyStockDbContext>
    {
        public EasyStockDbContext CreateDbContext(string[] args)
        {
            // Build configuration
            var builder = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddEnvironmentVariables();

            var configuration = builder.Build();

            var optionsBuilder = new DbContextOptionsBuilder<EasyStockDbContext>();
            var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? "Host=localhost;Port=5432;Database=easystock_db;Username=postgres;Password=postgres";

            optionsBuilder.UseNpgsql(connectionString);

            return new EasyStockDbContext(optionsBuilder.Options);
        }
    }
}
