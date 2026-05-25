using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using System.Diagnostics;

namespace EasyStock.Api.Observability.HealthChecks;

public sealed class RedisHealthCheck(IDistributedCache cache) : IHealthCheck
{
    private static readonly DistributedCacheEntryOptions CacheOptions = new()
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1)
    };

    // Latencia acima deste limite indica Redis sob pressao (Degraded), nao indisponivel
    private const int LatenciaDegradadaMs = 500;

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var sw = Stopwatch.StartNew();

            var value = DateTimeOffset.UtcNow.ToString("O");
            await cache.SetStringAsync("health:redis:ping", value, CacheOptions, cancellationToken);
            var result = await cache.GetStringAsync("health:redis:ping", cancellationToken);

            sw.Stop();

            if (result is null)
                return HealthCheckResult.Unhealthy("Redis aceitou escrita mas retornou null na leitura.");

            if (sw.ElapsedMilliseconds > LatenciaDegradadaMs)
                return HealthCheckResult.Degraded($"Redis respondeu mas com latencia elevada ({sw.ElapsedMilliseconds}ms).");

            return HealthCheckResult.Healthy($"Redis respondeu ao ping em {sw.ElapsedMilliseconds}ms.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Redis indisponivel.", ex);
        }
    }
}
