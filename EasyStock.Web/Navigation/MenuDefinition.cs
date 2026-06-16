namespace EasyStock.Web.Navigation;

/// <summary>
/// Item de navegacao do menu lateral. <see cref="Key"/> e kebab-case ESTAVEL:
/// renomear orfaniza o favorito que a referencia (o builder descarta orfaos).
/// <see cref="ActiveKeys"/> mapeia os valores legados de <c>ViewBag.ActiveMenuItem</c>
/// (inventario via grep dos controllers) para este item; todo valor emitido por um
/// controller DEVE constar em algum ActiveKeys (garantido por teste). <see cref="BadgeKey"/>
/// liga o item a uma contagem de <see cref="MenuBadges"/>.
/// </summary>
public sealed record MenuItem(
    string Key,
    string Label,
    string Icon,
    string Href,
    IReadOnlyList<string> ActiveKeys,
    string? BadgeKey = null,
    string? Tag = null,
    bool IsProducaoKds = false,
    bool IsExternal = false);

/// <summary>Grupo (accordion) do menu, com seus itens na ordem de exibicao.</summary>
public sealed record MenuGroup(
    string Key,
    string Label,
    string Icon,
    IReadOnlyList<MenuItem> Items);

/// <summary>
/// Estrutura canonica (estatica, imutavel) do menu lateral na nova IA por fluxo
/// (ADR-0032). Fonte unica da verdade: renomear/reordenar item = editar aqui.
/// Nenhuma rota muda em relacao ao menu antigo (Config Fiscal saiu para aba de
/// Configuracoes, fatia 8). Icones sao nomes do conjunto <c>es-icon</c>; a
/// correcao visual dos nomes e validada na fatia de render.
/// </summary>
public static class MenuDefinition
{
    // Chaves de badge — resolvidas contra MenuBadges no builder.
    public const string BadgePedidosAbertos = "pedidos-abertos";
    public const string BadgeProdutosCriticos = "produtos-criticos";
    public const string BadgeLotesVencidos = "lotes-vencidos";
    public const string BadgeDashboardTotal = "dashboard-total";

    /// <summary>Atalho fixo no topo (fora de grupo). Badge = soma criticos+vencidos.</summary>
    public static readonly MenuItem Dashboard = new(
        "dashboard", "Dashboard", "layout-dashboard", "/dashboard",
        new[] { "Dashboard" }, BadgeKey: BadgeDashboardTotal);

    public static readonly IReadOnlyList<MenuGroup> Groups = new[]
    {
        new MenuGroup("operacao", "Operação", "activity", new[]
        {
            new MenuItem("pedidos", "Pedidos", "shopping-cart", "/pedidos",
                new[] { "Pedidos", "PedidosMobile" }, BadgeKey: BadgePedidosAbertos),
            new MenuItem("kds-operacao", "KDS Operação", "chef-hat", "/kds",
                new[] { "Kds" }, IsProducaoKds: true),
            new MenuItem("kds-visor", "KDS Visor", "tv", "/pwa/#kds",
                Array.Empty<string>(), Tag: "alpha", IsProducaoKds: true, IsExternal: true),
            new MenuItem("caixa", "Caixa", "wallet", "/caixa",
                new[] { "Caixa", "CaixaMobile" }),
            new MenuItem("clientes", "Clientes", "users", "/clientes",
                new[] { "Clientes", "ClientesMobile" }),
            new MenuItem("cardapio", "Cardápio", "store", "/cardapio",
                new[] { "Cardapio" }),
        }),
        new MenuGroup("producao-estoque", "Produção e estoque", "boxes", new[]
        {
            new MenuItem("lotes-validade", "Lotes e validade", "layers", "/lotes",
                new[] { "Lotes", "LotesMobile" }, BadgeKey: BadgeLotesVencidos),
            new MenuItem("posicao-estoque", "Posição de estoque", "clipboard-list", "/estoque",
                new[] { "Estoque" }, BadgeKey: BadgeProdutosCriticos),
            new MenuItem("entradas", "Entradas", "arrow-down-to-line", "/entradas/historico",
                new[] { "Entradas" }),
            new MenuItem("saidas", "Saídas", "arrow-up-from-line", "/saidas/historico",
                new[] { "Saidas" }),
            new MenuItem("produtos", "Produtos", "package", "/produtos",
                new[] { "Produtos", "ProdutosMobile" }),
            new MenuItem("categorias", "Categorias", "tag", "/categorias",
                new[] { "Categorias" }),
        }),
        new MenuGroup("compras", "Compras", "shopping-bag", new[]
        {
            new MenuItem("pedidos-compra", "Pedidos de compra", "list-checks", "/listas-compras",
                new[] { "ListasCompras" }),
            new MenuItem("fornecedores", "Fornecedores", "truck", "/fornecedores",
                new[] { "Fornecedores" }),
        }),
        new MenuGroup("financeiro", "Financeiro", "landmark", new[]
        {
            new MenuItem("financeiro", "Visão geral", "bar-chart-3", "/financeiro",
                new[] { "Financeiro" }),
            new MenuItem("contas-receber", "Contas a receber", "banknote", "/contas-a-receber",
                new[] { "ContasAReceber" }),
            new MenuItem("contas-pagar", "Contas a pagar", "credit-card", "/contas-a-pagar",
                new[] { "ContasAPagar" }),
            new MenuItem("notas-fiscais", "Notas fiscais", "receipt", "/notas-fiscais",
                new[] { "NotasFiscais" }),
        }),
        new MenuGroup("crescimento", "Crescimento", "trending-up", new[]
        {
            new MenuItem("analises", "Análises", "line-chart", "/analytics",
                new[] { "Analytics", "Movimentacoes", "Inteligencia", "InteligenciaLojas" }),
            new MenuItem("relatorios", "Relatórios", "file-text", "/relatorios",
                new[] { "Relatorios" }),
            new MenuItem("anuncios", "Anúncios", "megaphone", "/anuncios",
                new[] { "Anuncios" }),
        }),
    };

    /// <summary>Rodape fixo (com borda superior). ConfiguracaoFiscal mapeia aqui (vira aba).</summary>
    public static readonly IReadOnlyList<MenuItem> Footer = new[]
    {
        new MenuItem("dispositivos", "Dispositivos", "monitor", "/dispositivos",
            new[] { "Dispositivos", "Operacao" }),
        new MenuItem("usuarios", "Usuários", "user-cog", "/usuarios",
            new[] { "Usuarios" }),
        new MenuItem("configuracoes", "Configurações", "settings", "/configuracoes",
            new[] { "Configuracoes", "Lojas", "Assinatura", "Notificacoes", "Preferencias", "ConfiguracaoFiscal" }),
    };

    /// <summary>Dashboard + itens de grupo + rodape, na ordem de declaracao (sem filtro de flag).</summary>
    public static IEnumerable<MenuItem> AllItems()
    {
        yield return Dashboard;
        foreach (var g in Groups)
            foreach (var i in g.Items)
                yield return i;
        foreach (var i in Footer)
            yield return i;
    }

    /// <summary>
    /// Seed do "Meu dia" para usuario sem preferencia salva, por perfil de negocio.
    /// Loja com producao/KDS recebe o fluxo completo; senao, o minimo. O backend
    /// aplica isto em runtime quando nao ha linha (nao persiste no 1o acesso).
    /// </summary>
    public static IReadOnlyList<string> DefaultFavoritos(bool kdsHabilitado) =>
        kdsHabilitado
            ? new[] { "pedidos", "kds-operacao", "lotes-validade", "posicao-estoque" }
            : new[] { "pedidos", "posicao-estoque" };
}
