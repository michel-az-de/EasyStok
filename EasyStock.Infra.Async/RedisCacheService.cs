using EasyStock.Application.Ports.Output;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Text.Json;

namespace EasyStock.Infra.Async;

/// <summary>
/// Implementação Redis do serviço de cache distribuído.
/// Usa IDistributedCache do ASP.NET Core com serialização JSON.
///
/// ⚠️ LIMITAÇÕES CONHECIDAS (DOCUMENTADAS, SEM CALLER EM PRODUÇÃO HOJE):
/// - <see cref="IncrementAsync"/> usa lock local por chave: atômico dentro do mesmo
///   processo mas NÃO entre instâncias. Para contador global exato em múltiplas
///   réplicas, introduzir dependência em <c>StackExchange.Redis</c> e usar
///   <c>IDatabase.StringIncrementAsync</c>. No estado atual essa API é apenas
///   exposta pelo port; nenhum use case em produção a consome — se passar a
///   haver contador crítico, MIGRAR antes de escalar horizontalmente.
/// - <see cref="SetExpiryAsync"/> não é suportado nativamente pelo IDistributedCache
///   (re-serialização do valor atual como workaround — ver implementação).
/// </summary>
public sealed class RedisCacheService(IDistributedCache cache, ILogger<RedisCacheService> logger) : ICacheService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    // #282: timeout defensivo no lock de incremento. Aqui a seção crítica faz I/O
    // real ao Redis (await Get/Set), então sob contenção o timeout pode de fato
    // disparar; quando dispara, propaga (não mascara) em vez de pendurar a thread.
    private static readonly TimeSpan LockTimeout = TimeSpan.FromSeconds(5);

    // Locks por-chave para serialização do increment dentro do mesmo processo.
    // Evita pior caso de race condition sem introduzir dependência do StackExchange.Redis.
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> IncrementLocks = new();

    private static SemaphoreSlim GetLock(string key) =>
        IncrementLocks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));

    /// <summary>Valida que a chave de cache não é nula ou vazia.</summary>
    private static void ValidarChave(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("A chave não pode ser nula ou vazia.", nameof(key));
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan? ttl = null)
    {
        ValidarChave(key);

        var json = JsonSerializer.Serialize(value, JsonOptions);
        var options = ttl.HasValue
            ? new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = ttl }
            : new DistributedCacheEntryOptions();

        await cache.SetStringAsync(key, json, options);
    }

    public async Task<T?> GetAsync<T>(string key)
    {
        ValidarChave(key);

        var json = await cache.GetStringAsync(key);
        return json is null ? default : JsonSerializer.Deserialize<T>(json, JsonOptions);
    }

    public async Task RemoveAsync(string key)
    {
        ValidarChave(key);

        await cache.RemoveAsync(key);
    }

    public async Task<bool> ExistsAsync(string key)
    {
        ValidarChave(key);

        // A existência da chave não depende do conteúdo serializado.
        var cachedValue = await cache.GetAsync(key);
        return cachedValue is not null;
    }

    public async Task<long> IncrementAsync(string key, long value = 1)
    {
        ValidarChave(key);

        if (value == 0)
            return (await GetAsync<long?>(key)) ?? 0L;

        // Serializa o read-modify-write por chave dentro do processo.
        // NOTA: em ambientes multi-instância (ex.: Kubernetes com HPA) isso não
        // protege entre pods; substituir por StringIncrementAsync do
        // StackExchange.Redis se a aplicação for escalada horizontalmente.
        var semaphore = GetLock(key);
        if (!await semaphore.WaitAsync(LockTimeout))
        {
            logger.LogWarning(
                "RedisCacheService: timeout de {Timeout}s aguardando lock de incremento da chave '{Key}'.",
                LockTimeout.TotalSeconds, key);
            throw new TimeoutException(
                $"Timeout ao adquirir lock de incremento para a chave '{key}'.");
        }
        try
        {
            var current = (await GetAsync<long?>(key)) ?? 0L;
            var newValue = checked(current + value);
            await SetAsync(key, newValue);
            return newValue;
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

    public async Task SetExpiryAsync(string key, TimeSpan ttl)
    {
        ValidarChave(key);

        if (ttl <= TimeSpan.Zero)
            throw new ArgumentException("TTL must be positive", nameof(ttl));

        // IDistributedCache não suporta alterar TTL diretamente
        // Estratégia alternativa: re-setar o valor com novo TTL
        // ⚠️ LIMITAÇÃO: só funciona se o tipo for serializável e não causar perda de dados

        try
        {
            var json = await cache.GetStringAsync(key);
            if (!string.IsNullOrEmpty(json))
            {
                var options = new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = ttl
                };
                await cache.SetStringAsync(key, json, options);
            }
        }
        catch (Exception ex)
        {
            throw new NotSupportedException(
                $"SetExpiryAsync falhou para chave '{key}': {ex.Message}. " +
                "IDistributedCache não suporta alteração de TTL nativamente.", ex);
        }
    }

    public async Task RemoveAsync(IEnumerable<string> keys)
    {
        if (keys == null)
            throw new ArgumentNullException(nameof(keys));

        var keyArray = keys.ToArray();
        if (keyArray.Length == 0)
            return;

        // Valida todas as chaves antes de processar
        foreach (var key in keyArray)
        {
            if (string.IsNullOrWhiteSpace(key))
                throw new ArgumentException("Keys cannot contain null or empty values", nameof(keys));
        }

        var tasks = keyArray.Select(key => cache.RemoveAsync(key));
        await Task.WhenAll(tasks);
    }
}
