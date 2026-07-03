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
    /// case-insensitive, ignora fragmento e trailing slash; vence o item com MAIS
    /// segmentos. Querystring so DESEMPATA irmaos com o mesmo path (596996ab deu
    /// href /estoque?status=vencido ao lotes-validade e /estoque passou a ser
    /// compartilhado): href cuja query casa com a URL atual ganha; query que nao
    /// casa perde pro irmao sem query. Sem casamento de href, fallback para o
    /// ActiveMenuItem legado contra os ActiveKeys. Nenhum casamento => null.
    /// </summary>
    internal static string? ResolveActive(
        IReadOnlyList<MenuItem> navigable, string? currentPath, string? activeMenuItem)
    {
        var segs = Segments(currentPath);
        if (segs.Length > 0)
        {
            MenuItem? best = null;
            var bestScore = 0;
            foreach (var item in navigable)
            {
                if (item.IsExternal) continue;
                var itemSegs = Segments(item.Href);
                if (itemSegs.Length == 0 || itemSegs.Length > segs.Length)
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
                if (!match) continue;

                // Segmentos dominam (*4 > bonus de query); a query so decide empates.
                var score = itemSegs.Length * 4 + QueryScore(item.Href, currentPath);
                if (score > bestScore)
                {
                    best = item;
                    bestScore = score;
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

    // +2 se TODOS os pares da query do href estao na URL atual; -1 se o href tem
    // query que nao casa; 0 se o href nao tem query.
    private static int QueryScore(string? href, string? currentPath)
    {
        var hrefQuery = QueryPairs(href);
        if (hrefQuery.Count == 0) return 0;
        var atual = QueryPairs(currentPath);
        return hrefQuery.All(atual.Contains) ? 2 : -1;
    }

    private static HashSet<string> QueryPairs(string? path)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(path)) return set;
        var q = path.IndexOf('?');
        if (q < 0 || q == path.Length - 1) return set;
        var frag = path.IndexOf('#', q);
        var qs = frag > q ? path[(q + 1)..frag] : path[(q + 1)..];
        foreach (var par in qs.Split('&', StringSplitOptions.RemoveEmptyEntries))
            set.Add(par);
        return set;
    }
}
