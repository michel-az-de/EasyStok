using Microsoft.Extensions.Diagnostics.HealthChecks;
using MongoDB.Bson;
using MongoDB.Driver;

namespace EasyStock.Infra.MongoDb.HealthChecks;

public sealed class MongoDatabaseHealthCheck(IMongoClient mongoClient, string databaseName) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await mongoClient.GetDatabase(databaseName)
                .RunCommandAsync((Command<BsonDocument>)"{ ping: 1 }", cancellationToken: cancellationToken);

            return HealthCheckResult.Healthy("MongoDB respondeu ao ping.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Falha ao consultar MongoDB.", ex);
        }
    }
}
