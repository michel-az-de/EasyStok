using EasyStock.Application.Ports.Output;
using Microsoft.Extensions.Caching.Distributed;
using System.Text.Json;

namespace EasyStock.Infra.Async;

/// <summary>
/// Implementação Redis do serviço de cache distribuído.
/// Usa IDistributedCache do ASP.NET Core com serialização JSON.
///
/// ⚠️ LIMITAÇÕES CONHECIDAS:
/// - IncrementAsync não é atômico (devido a limitações do IDistributedCache)
/// - SetExpiryAsync não é suportado
/// - Para operações atômicas em produção, considere usar StackExchange.Redis diretamente
/// </summary>
public sealed class RedisCacheService(IDistributedCache cache) : ICacheService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

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

        // ⚠️ LIMITAÇÃO CRÍTICA: IDistributedCache não suporta operações atômicas nativas
        // Esta implementação NÃO é thread-safe e pode causar race conditions em ambientes concorrentes
        // Para produção com alta concorrência, considere:
        // 1. Usar IConnectionMultiplexer do StackExchange.Redis diretamente
        // 2. Implementar lock distribuído (ex: Redis Lock)
        // 3. Usar operações atômicas específicas do provedor

        if (value == 0)
            return (await GetAsync<long?>(key)) ?? 0L;

        try
        {
            var current = (await GetAsync<long?>(key)) ?? 0L;
            var newValue = checked(current + value); // Previne overflow

            // Não força TTL implícito durante incremento para evitar alteração inesperada
            // no ciclo de vida da chave.
            await SetAsync(key, newValue);

            return newValue;
        }
        catch (OverflowException)
        {
            throw new InvalidOperationException($"Incremento causaria overflow no counter '{key}'");
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
