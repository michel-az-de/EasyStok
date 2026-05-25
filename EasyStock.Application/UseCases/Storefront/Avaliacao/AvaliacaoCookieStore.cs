using System.Security.Cryptography;
using System.Text;
using EasyStock.Application.Ports.Output;

namespace EasyStock.Application.UseCases.Storefront.Avaliacao;

/// <summary>
/// Armazena e valida o vínculo entre cookie <c>__Host-cdb_aval_{pedidoId}</c>
/// e o pedido correspondente via <see cref="ICacheService"/> (TTL 30 dias).
///
/// <para>
/// Persiste apenas o SHA-256 do valor do cookie — o valor bruto nunca fica em memória
/// server-side além do momento de emissão.
/// </para>
/// </summary>
public sealed class AvaliacaoCookieStore(ICacheService cache)
{
    private static readonly TimeSpan Ttl = TimeSpan.FromDays(30);

    public static string CacheKey(Guid pedidoId) => $"aval_cookie:{pedidoId}";

    public static string ComputeHash(string cookieValue)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(cookieValue));
        return Convert.ToBase64String(bytes);
    }

    /// <summary>Registra o cookie emitido para o pedido.</summary>
    public Task RegistrarAsync(Guid pedidoId, string cookieValue) =>
        cache.SetAsync(CacheKey(pedidoId), ComputeHash(cookieValue), Ttl);

    /// <summary>
    /// Verifica se o cookie recebido é válido para o pedido.
    /// Retorna false se chave não existe ou hash não confere.
    /// </summary>
    public async Task<bool> EhValidoAsync(Guid pedidoId, string cookieValue)
    {
        var storedHash = await cache.GetAsync<string>(CacheKey(pedidoId));
        if (storedHash is null) return false;
        return storedHash == ComputeHash(cookieValue);
    }
}
