using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace EasyStock.Api.Observability.HealthChecks;

public sealed class RedisHealthCheck(IDistributedCache cache) : IHealthCheck
{
    private static readonly DistributedCacheEntryOptions CacheOptions = new()
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1)
    };

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var value = DateTimeOffset.UtcNow.ToString("O");
            await cache.SetStringAsync("health:redis:ping", value, CacheOptions, cancellationToken);
            var result = await cache.GetStringAsync("health:redis:ping", cancellationToken);

            return result is not null
                ? HealthCheckResult.Healthy("Redis respondeu ao ping.")
                : HealthCheckResult.Degraded("Redis aceitou escrita mas retornou null na leitura.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Degraded("Redis indisponivel.", ex);
        }
    }
}
