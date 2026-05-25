using EasyStock.Application.Ports.Output;
using Microsoft.Extensions.Caching.Memory;

namespace EasyStock.Infra.Async;

/// <summary>
/// Implementacao in-memory de <see cref="ICacheService"/> usando
/// <see cref="IMemoryCache"/> do framework. Serve como fallback quando
/// Redis/IDistributedCache nao esta configurado (dev local, single-instance).
///
/// <para>
/// Em multi-instancia (ex: Azure App Service com >1 worker, Kubernetes com
/// replicas) a cache fica isolada por processo — quem precisar de cache
/// global cross-instancia deve usar <see cref="RedisCacheService"/>.
/// </para>
/// </summary>
public sealed class InMemoryCacheService(IMemoryCache cache) : ICacheService
{
    private static void ValidarChave(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("A chave nao pode ser nula ou vazia.", nameof(key));
    }

    public Task SetAsync<T>(string key, T value, TimeSpan? ttl = null)
    {
        ValidarChave(key);
        var entry = cache.CreateEntry(key);
        entry.Value = value;
        if (ttl.HasValue) entry.AbsoluteExpirationRelativeToNow = ttl;
        entry.Dispose();
        return Task.CompletedTask;
    }

    public Task<T?> GetAsync<T>(string key)
    {
        ValidarChave(key);
        return Task.FromResult(cache.TryGetValue<T>(key, out var value) ? value : default);
    }

    public Task RemoveAsync(string key)
    {
        ValidarChave(key);
        cache.Remove(key);
        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string key)
    {
        ValidarChave(key);
        return Task.FromResult(cache.TryGetValue(key, out _));
    }

    public Task<long> IncrementAsync(string key, long value = 1)
    {
        ValidarChave(key);
        // Serializa por chave — IMemoryCache nao tem incremento atomico nativo.
        var semaphore = GetLock(key);
        semaphore.Wait();
        try
        {
            var current = cache.TryGetValue<long>(key, out var v) ? v : 0L;
            var novo = checked(current + value);
            cache.Set(key, novo);
            return Task.FromResult(novo);
        }
        catch (OverflowException)
        {
            throw new InvalidOperationException($"Incremento causaria overflow no counter '{key}'");
        }
        finally
        {
            semaphore.Release();
        }
    }

    public Task SetExpiryAsync(string key, TimeSpan ttl)
    {
        ValidarChave(key);
        if (ttl <= TimeSpan.Zero)
            throw new ArgumentException("TTL precisa ser positivo.", nameof(ttl));
        if (cache.TryGetValue(key, out var current))
        {
            cache.Set(key, current!, new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = ttl });
        }
        return Task.CompletedTask;
    }

    public Task RemoveAsync(IEnumerable<string> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        foreach (var key in keys)
        {
            if (string.IsNullOrWhiteSpace(key))
                throw new ArgumentException("Keys cannot contain null or empty values", nameof(keys));
            cache.Remove(key);
        }
        return Task.CompletedTask;
    }

    // Mesma estrategia do RedisCacheService: lock por chave em-processo.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, SemaphoreSlim> Locks = new();
    private static SemaphoreSlim GetLock(string key) => Locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
}
