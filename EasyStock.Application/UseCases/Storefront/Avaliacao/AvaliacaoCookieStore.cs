using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Caching.Memory;

namespace EasyStock.Application.UseCases.Storefront.Avaliacao;

/// <summary>
/// Armazena e valida o vínculo entre cookie <c>__Host-cdb_aval_{pedidoId}</c>
/// e o pedido correspondente via IMemoryCache (TTL 30 dias).
///
/// <para>
/// Persiste apenas o SHA-256 do valor do cookie — o valor bruto nunca fica em memória
/// server-side além do momento de emissão.
/// </para>
/// </summary>
public sealed class AvaliacaoCookieStore(IMemoryCache cache)
{
    private static readonly TimeSpan Ttl = TimeSpan.FromDays(30);

    public static string CacheKey(Guid pedidoId) => $"aval_cookie:{pedidoId}";

    public static string ComputeHash(string cookieValue)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(cookieValue));
        return Convert.ToBase64String(bytes);
    }

    /// <summary>Registra o cookie emitido para o pedido.</summary>
    public void Registrar(Guid pedidoId, string cookieValue) =>
        cache.Set(CacheKey(pedidoId), ComputeHash(cookieValue), Ttl);

    /// <summary>
    /// Verifica se o cookie recebido é válido para o pedido.
    /// Retorna false se chave não existe ou hash não confere.
    /// </summary>
    public bool EhValido(Guid pedidoId, string cookieValue)
    {
        if (!cache.TryGetValue(CacheKey(pedidoId), out string? storedHash))
            return false;
        return storedHash == ComputeHash(cookieValue);
    }
}
