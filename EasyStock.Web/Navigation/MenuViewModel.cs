namespace EasyStock.Web.Navigation;

/// <summary>
/// Contagens dinamicas dos badges, vindas do BFF <c>/menu/resumo</c> (fatia 2):
/// pedidos em aberto, produtos criticos, lotes vencidos com saldo. Mesma fonte do
/// painel "Atencao" do dashboard.
/// </summary>
public sealed record MenuBadges(int PedidosAbertos, int ProdutosCriticos, int LotesVencidos)
{
    public static readonly MenuBadges Zero = new(0, 0, 0);

    /// <summary>Soma exibida no badge do Dashboard: criticos + vencidos (pedidos NAO entram).</summary>
    public int DashboardTotal => ProdutosCriticos + LotesVencidos;
}

/// <summary>Item ja resolvido para render: ativo-por-rota e contagem do badge.</summary>
public sealed record MenuItemView(MenuItem Item, bool IsActive, int Badge)
{
    public string Key => Item.Key;
    public bool HasBadge => Badge > 0;
}

/// <summary>Grupo resolvido: itens, estado aberto (pela rota) e soma dos badges filhos.</summary>
public sealed record MenuGroupView(MenuGroup Group, IReadOnlyList<MenuItemView> Items, bool IsOpen)
{
    /// <summary>Soma dos badges dos filhos — exibida no header quando o grupo esta fechado.</summary>
    public int BadgeSum { get; init; }
    public bool HasBadge => BadgeSum > 0;
}

/// <summary>
/// Modelo resolvido do menu para o TagHelper renderizar: Dashboard, "Meu dia"
/// (favoritos), grupos (accordion) e rodape. <see cref="ActiveKey"/> e a chave do
/// item ativo derivado da rota (ou null quando nada casa).
/// </summary>
public sealed record MenuViewModel(
    MenuItemView Dashboard,
    IReadOnlyList<MenuItemView> MeuDia,
    IReadOnlyList<MenuGroupView> Groups,
    IReadOnlyList<MenuItemView> Footer,
    string? ActiveKey);
