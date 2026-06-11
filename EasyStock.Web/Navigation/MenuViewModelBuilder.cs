namespace EasyStock.Web.Navigation;

/// <summary>
/// Builder PURO (sem HttpContext) que resolve <see cref="MenuViewModel"/> a partir
/// da rota atual, do ActiveMenuItem legado, dos favoritos e dos badges. Toda a
/// logica testavel do menu vive aqui (ADR-0032). Pipeline na ordem (P1-6):
/// (1) filtra a arvore pela flag KDS; (2) resolve ativo e favoritos contra a arvore
/// JA filtrada (favorito orfao some sozinho).
/// </summary>
public static class MenuViewModelBuilder
{
    public const int MaxFavoritos = 20;

    public static MenuViewModel Build(
        string? currentPath,
        string? activeMenuItem,
        IReadOnlyList<string>? favoritosKeys,
        MenuBadges? badges,
        bool kdsHabilitado)
    {
        badges ??= MenuBadges.Zero;

        // (1) filtra a arvore pela flag KDS — itens de producao somem quando off.
        bool Visible(MenuItem i) => kdsHabilitado || !i.IsProducaoKds;

        var groups = MenuDefinition.Groups
            .Select(g => (group: g, items: g.Items.Where(Visible).ToList()))
            .ToList();

        // Itens navegaveis visiveis (Dashboard + grupos filtrados + rodape): base do
        // matching e da resolucao de favoritos.
        var navigable = new List<MenuItem> { MenuDefinition.Dashboard };
        navigable.AddRange(groups.SelectMany(x => x.items));
        navigable.AddRange(MenuDefinition.Footer);

        // (2) item ativo: rota por segmentos; fallback ActiveMenuItem legado.
        var activeKey = ResolveActive(navigable, currentPath, activeMenuItem);

        // Grupo que contem o ativo (accordion exclusivo abre esse no load).
        var activeGroupKey = groups
            .FirstOrDefault(x => x.items.Any(i => i.Key == activeKey))
            .group?.Key;

        int BadgeOf(MenuItem i) => i.BadgeKey switch
        {
            MenuDefinition.BadgePedidosAbertos => badges.PedidosAbertos,
            MenuDefinition.BadgeProdutosCriticos => badges.ProdutosCriticos,
            MenuDefinition.BadgeLotesVencidos => badges.LotesVencidos,
            MenuDefinition.BadgeDashboardTotal => badges.DashboardTotal,
            _ => 0,
        };

        MenuItemView ToView(MenuItem i) => new(i, i.Key == activeKey, BadgeOf(i));

        var groupViews = groups.Select(x =>
        {
            var items = x.items.Select(ToView).ToList();
            return new MenuGroupView(x.group, items, x.group.Key == activeGroupKey)
            {
                BadgeSum = items.Sum(v => v.Badge),
            };
        }).ToList();

        // (3) favoritos contra a arvore filtrada: dedup preservando ordem, cap 20,
        // descarta orfaos (chave inexistente, item escondido pela flag, ou externo).
        var byKey = navigable.ToDictionary(i => i.Key);
        var meuDia = new List<MenuItemView>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var key in favoritosKeys ?? Array.Empty<string>())
        {
            if (meuDia.Count >= MaxFavoritos) break;
            if (string.IsNullOrEmpty(key) || !seen.Add(key)) continue;
            if (byKey.TryGetValue(key, out var item) && !item.IsExternal)
                meuDia.Add(ToView(item));
        }

        return new MenuViewModel(
            ToView(MenuDefinition.Dashboard),
            meuDia,
            groupViews,
            MenuDefinition.Footer.Select(ToView).ToList(),
            activeKey);
    }

    /// <summary>
    /// Matching ativo-por-rota: por SEGMENTOS de path (nunca prefixo cru de string),
    /// case-insensitive, ignora querystring/fragmento e trailing slash; vence o item
    /// com MAIS segmentos. Sem casamento de href, faz fallback para o ActiveMenuItem
    /// legado contra os ActiveKeys. Nenhum casamento => null (nada ativo).
    /// </summary>
    internal static string? ResolveActive(
        IReadOnlyList<MenuItem> navigable, string? currentPath, string? activeMenuItem)
    {
        var segs = Segments(currentPath);
        if (segs.Length > 0)
        {
            MenuItem? best = null;
            var bestLen = 0;
            foreach (var item in navigable)
            {
                if (item.IsExternal) continue;
                var itemSegs = Segments(item.Href);
                if (itemSegs.Length == 0 || itemSegs.Length > segs.Length || itemSegs.Length <= bestLen)
                    continue;

                var match = true;
                for (var k = 0; k < itemSegs.Length; k++)
                {
                    if (!string.Equals(itemSegs[k], segs[k], StringComparison.OrdinalIgnoreCase))
                    {
                        match = false;
                        break;
                    }
                }

                if (match)
                {
                    best = item;
                    bestLen = itemSegs.Length;
                }
            }

            if (best is not null) return best.Key;
        }

        if (!string.IsNullOrEmpty(activeMenuItem))
        {
            foreach (var item in navigable)
                if (item.ActiveKeys.Contains(activeMenuItem, StringComparer.OrdinalIgnoreCase))
                    return item.Key;
        }

        return null;
    }

    private static string[] Segments(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return Array.Empty<string>();

        var p = path;
        var cut = p.IndexOfAny(new[] { '?', '#' });
        if (cut >= 0) p = p[..cut];

        return p.Split('/', StringSplitOptions.RemoveEmptyEntries);
    }
}
