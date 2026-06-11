namespace EasyStock.Web.Models.Api;

/// <summary>Resposta do GET /api/preferencias/menu-favoritos. Favoritos null = sem linha (seed).</summary>
public record FavoritosMenuApi
{
    public List<string>? Favoritos { get; init; }
    public bool KdsHabilitado { get; init; }
}

/// <summary>Resposta do PUT (lista normalizada p/ a UI otimista reconciliar).</summary>
public record FavoritosMenuPutApi
{
    public List<string>? Favoritos { get; init; }
}
