using EasyStock.Application.Ports.Output;
using Microsoft.Extensions.Caching.Distributed;
using System.Text.Json;

namespace EasyStock.Infra.Async;

/// <summary>
/// Implementação Redis do serviço de cache distribuído.
/// Usa IDistributedCache do ASP.NET Core com serialização JSON.
/// </summary>
public sealed class RedisCacheService(IDistributedCache cache) : ICacheService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public async Task SetAsync<T>(string key, T value, TimeSpan? ttl = null)
    {
        var json = JsonSerializer.Serialize(value, JsonOptions);
        var options = ttl.HasValue
            ? new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = ttl }
            : new DistributedCacheEntryOptions();

        await cache.SetStringAsync(key, json, options);
    }

    public async Task<T?> GetAsync<T>(string key)
    {
        var json = await cache.GetStringAsync(key);
        return json is null ? default : JsonSerializer.Deserialize<T>(json, JsonOptions);
    }

    public Task RemoveAsync(string key) =>
        cache.RemoveAsync(key);

    public async Task<bool> ExistsAsync(string key)
    {
        var value = await cache.GetAsync(key);
        return value is not null;
    }

    public async Task<long> IncrementAsync(string key, long value = 1)
    {
        // Redis incrementa diretamente, mas IDistributedCache não suporta
        // Implementação básica usando get/set
        var current = (await GetAsync<long?>(key)) ?? 0L;
        var newValue = current + value;
        await SetAsync(key, newValue);
        return newValue;
    }

    public Task SetExpiryAsync(string key, TimeSpan ttl)
    {
        // IDistributedCache não suporta alterar TTL diretamente
        // Esta é uma limitação da abstração do ASP.NET Core
        throw new NotSupportedException("SetExpiryAsync não é suportado pelo IDistributedCache do ASP.NET Core");
    }

    public async Task RemoveAsync(IEnumerable<string> keys)
    {
        var tasks = keys.Select(key => cache.RemoveAsync(key));
        await Task.WhenAll(tasks);
    }
}
