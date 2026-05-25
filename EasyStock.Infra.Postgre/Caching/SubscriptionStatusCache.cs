using EasyStock.Application.Ports.Output.Caching;
using EasyStock.Infra.Postgre.Configuration;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace EasyStock.Infra.Postgre.Caching;

/// <summary>
/// Implementacao in-memory de <see cref="ISubscriptionStatusCache"/> sobre
/// <see cref="IMemoryCache"/>. TTL controlado por <c>CacheOptions.SubscriptionStatusDuration</c>
/// (default 60s). Singleton seguro para uso concorrente — IMemoryCache e thread-safe.
///
/// <para>
/// O cache armazena <see cref="SubscriptionStatusSnapshot"/> (record imutavel)
/// e nunca a entidade <c>AssinaturaEmpresa</c> rastreada por DbContext —
/// reuso cross-request de entidade EF rastreada quebraria UoW.
/// </para>
/// </summary>
public sealed class SubscriptionStatusCache(IMemoryCache cache, IOptions<CacheOptions> options)
    : ISubscriptionStatusCache
{
    private const string KeyPrefix = "subgate:status:";
    private readonly TimeSpan _ttl = options.Value.SubscriptionStatusDuration > TimeSpan.Zero
        ? options.Value.SubscriptionStatusDuration
        : TimeSpan.FromSeconds(60);

    public async Task<SubscriptionStatusSnapshot?> GetOrFetchAsync(
        Guid empresaId,
        Func<CancellationToken, Task<SubscriptionStatusSnapshot?>> fetch,
        CancellationToken ct = default)
    {
        if (empresaId == Guid.Empty)
            return await fetch(ct);

        var key = KeyPrefix + empresaId.ToString("N");
        if (cache.TryGetValue(key, out SubscriptionStatusSnapshot? cached))
            return cached is { NaoEncontrada: true } ? null : cached;

        var fresh = await fetch(ct);
        // Cacheia inclusive o "nao encontrada" (sentinel Vazio) — evita
        // bater no DB toda request quando tenant nao tem assinatura ativa.
        // Caller continua recebendo null nesse caso.
        cache.Set(key, fresh ?? SubscriptionStatusSnapshot.Vazio, _ttl);
        return fresh;
    }

    public void Invalidate(Guid empresaId)
    {
        if (empresaId == Guid.Empty) return;
        cache.Remove(KeyPrefix + empresaId.ToString("N"));
    }
}
