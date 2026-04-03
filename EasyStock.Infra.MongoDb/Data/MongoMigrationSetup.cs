using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using System.IO;

namespace EasyStock.Infra.MongoDb.Data
{
    public static class MongoMigrationSetup
    {
        public static void Configure(IServiceCollection services)
        {
            var builder = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: true)
                .AddEnvironmentVariables();
            var config = builder.Build();

            // Placeholder: integration with a MongoDB migration library (e.g. Mongo.Migration) can be added here.
            // The current project includes Mongo.Migration package, but the startup extension may require
            // additional setup depending on the package version. Configure migrations in application startup
            // and register services as appropriate.
        }
    }
}
