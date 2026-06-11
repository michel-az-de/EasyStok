using EasyStock.Web.Models.Api;
using Microsoft.Extensions.Caching.Memory;

namespace EasyStock.Web.Services;

/// <summary>Favoritos do menu já resolvidos p/ o Web (ADR-0032 fatia 4b).
/// Favoritos null = sem linha (o builder aplica o seed por <see cref="KdsHabilitado"/>).</summary>
public sealed record MenuFavoritosBff(IReadOnlyList<string>? Favoritos, bool KdsHabilitado);

/// <summary>Seam testável p/ a Api de favoritos (claims-only — a Api resolve usuario/empresa do JWT).</summary>
public interface IPreferenciaMenuFonte
{
    Task<(FavoritosMenuApi? Data, bool Ok)> ObterAsync(string? lojaId);
    Task<(IReadOnlyList<string>? Favoritos, bool Ok)> SalvarAsync(string? lojaId, IReadOnlyList<string> favoritos);
}

public sealed class PreferenciaMenuFonte(ApiClient api) : IPreferenciaMenuFonte
{
    public async Task<(FavoritosMenuApi?, bool)> ObterAsync(string? lojaId)
    {
        var r = await api.GetAsync<FavoritosMenuApi>(
            $"preferencias/menu-favoritos?lojaId={Uri.EscapeDataString(lojaId ?? string.Empty)}");
        return (r.Success ? r.Data : null, r.Success);
    }

    public async Task<(IReadOnlyList<string>?, bool)> SalvarAsync(string? lojaId, IReadOnlyList<string> favoritos)
    {
        var body = new { lojaId = Guid.TryParse(lojaId, out var g) ? g : Guid.Empty, favoritos };
        var r = await api.PutAsync<FavoritosMenuPutApi>("preferencias/menu-favoritos", body);
        return (r.Success ? (r.Data?.Favoritos ?? favoritos) : null, r.Success);
    }
}

/// <summary>
/// BFF dos favoritos do menu: cache 5min por usuario+loja, invalidado no PUT (D2;
/// seguro com 1 instancia Web — caveat de escala no ADR). Recebe usuarioId/lojaId por
/// parametro (o controller/TagHelper le da sessao) p/ ficar puro e testavel. Degrada
/// sem derrubar a pagina: falha => favoritos null + KdsHabilitado false (seed conservador).
/// </summary>
public sealed class PreferenciaMenuService(IPreferenciaMenuFonte fonte, IMemoryCache cache)
{
    public static readonly TimeSpan Ttl = TimeSpan.FromMinutes(5);

    private static string Key(string? usuarioId, string? lojaId) => $"fav:{usuarioId}:{lojaId}";

    public async Task<MenuFavoritosBff> ObterAsync(string? usuarioId, string? lojaId)
    {
        var key = Key(usuarioId, lojaId);
        if (cache.TryGetValue(key, out MenuFavoritosBff? cached) && cached is not null)
            return cached;

        var (data, ok) = await fonte.ObterAsync(lojaId);
        var result = ok && data is not null
            ? new MenuFavoritosBff(data.Favoritos, data.KdsHabilitado)
            : new MenuFavoritosBff(null, false);

        if (ok) cache.Set(key, result, Ttl); // nunca cacheia falha
        return result;
    }

    /// <summary>Persiste e invalida o cache. Devolve a lista normalizada, ou null em falha.</summary>
    public async Task<IReadOnlyList<string>?> SalvarAsync(string? usuarioId, string? lojaId, IReadOnlyList<string> favoritos)
    {
        var (norm, ok) = await fonte.SalvarAsync(lojaId, favoritos);
        if (ok) cache.Remove(Key(usuarioId, lojaId));
        return ok ? norm : null;
    }
}
