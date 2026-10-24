using Microsoft.Extensions.Caching.Memory;

namespace EasyStock.Api.Services.Storefront;

public sealed class CardapioPublicoCache(IMemoryCache cache) : ICardapioPublicoCache
{
    /// <summary>A chave é o slug como veio: o filtro do tenant compara a caixa exata e outra grafia nunca entra no cache.</summary>
    public static string Chave(string slug) => $"storefront:menu:{slug}";

    public void Invalidar(string slug)
    {
        if (!string.IsNullOrWhiteSpace(slug)) cache.Remove(Chave(slug));
    }
}
