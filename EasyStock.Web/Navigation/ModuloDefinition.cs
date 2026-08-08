namespace EasyStock.Web.Navigation;

/// <summary>
/// Modulos do shell modular (ADR-0046): cada modulo e a face de um grupo do
/// <see cref="MenuDefinition"/> no portal (Launcher). O rodape — que nao e grupo —
/// vira o modulo virtual <c>admin</c>.
///
/// <para>
/// O <see cref="MenuDefinition"/> segue sendo a fonte unica da estrutura (ADR-0032):
/// aqui so existe o mapeamento modulo -> grupo. Um teste de guarda amarra os dois,
/// para que renomear a key de um grupo nao orfanize um modulo em silencio.
/// </para>
///
/// <para>
/// O modulo ativo NAO e estado: e derivado da rota via <see cref="ResolverPorRota"/>.
/// Nao ha querystring, cookie nem sessao — o grupo do item ativo E o modulo, entao o
/// filtro sobrevive a redirect, form, paginacao e link de conteudo.
/// </para>
/// </summary>
public static class ModuloDefinition
{
    /// <summary>Chave do modulo virtual que representa o rodape (Dispositivos/Usuarios/Configuracoes).</summary>
    public const string ModuloAdmin = "admin";

    /// <summary>
    /// Modulos do portal, na ordem de exibicao. A ordem espelha o menu: os 5 grupos
    /// do <see cref="MenuDefinition.Groups"/> e, por ultimo, o rodape como Administracao.
    /// </summary>
    public static IReadOnlyList<ModuloInfo> Modulos { get; } =
    [
        new("operacao", "Operação", "activity", "Pedidos, KDS, caixa, clientes e cardápio", "/pedidos"),
        new("producao", "Produção e Estoque", "boxes", "Validade, posição, entradas, saídas e produtos", "/estoque?status=vencido"),
        new("compras", "Compras", "shopping-bag", "Pedidos de compra e fornecedores", "/listas-compras"),
        new("financeiro", "Financeiro", "landmark", "Contas a receber, pagar e notas fiscais", "/financeiro"),
        new("crescimento", "Crescimento", "trending-up", "Análises, relatórios e anúncios", "/analytics"),
        new(ModuloAdmin, "Administração", "settings", "Dispositivos, usuários e configurações", "/configuracoes"),
    ];

    /// <summary>
    /// Chave do grupo do <see cref="MenuDefinition"/> que o modulo representa.
    /// <c>admin</c> devolve null por nao ser grupo: e o rodape, tratado a parte pelo builder.
    /// Modulo desconhecido tambem devolve null.
    /// </summary>
    public static string? GrupoDoModulo(string? moduloKey) => moduloKey switch
    {
        "operacao" => "operacao",
        "producao" => "producao-estoque",
        "compras" => "compras",
        "financeiro" => "financeiro",
        "crescimento" => "crescimento",
        _ => null,
    };

    /// <summary>Caminho inverso: chave do grupo -> chave do modulo. Grupo desconhecido devolve null.</summary>
    public static string? ModuloDoGrupo(string? grupoKey) => grupoKey switch
    {
        "operacao" => "operacao",
        "producao-estoque" => "producao",
        "compras" => "compras",
        "financeiro" => "financeiro",
        "crescimento" => "crescimento",
        _ => null,
    };

    /// <summary>Metadados do modulo pela chave, ou null se a chave nao existir.</summary>
    public static ModuloInfo? PorChave(string? moduloKey) =>
        string.IsNullOrEmpty(moduloKey)
            ? null
            : Modulos.FirstOrDefault(m => m.Key == moduloKey);

    /// <summary>
    /// Modulo em que a rota atual esta, ou null quando a rota nao pertence a modulo
    /// nenhum (Dashboard, portal, landing, rotas sem dono) — nesse caso o menu aparece
    /// inteiro (fail-open). Reusa o mesmo matching ativo-por-rota do menu, entao o
    /// modulo nunca discorda do item que o menu marca como ativo.
    /// </summary>
    public static string? ResolverPorRota(string? currentPath, string? activeMenuItem)
    {
        var itens = MenuDefinition.AllItems().ToList();
        var activeKey = MenuViewModelBuilder.ResolveActive(itens, currentPath, activeMenuItem);
        if (activeKey is null) return null;

        if (MenuDefinition.Footer.Any(i => i.Key == activeKey))
            return ModuloAdmin;

        var grupo = MenuDefinition.Groups.FirstOrDefault(g => g.Items.Any(i => i.Key == activeKey));
        return grupo is null ? null : ModuloDoGrupo(grupo.Key);
    }
}

/// <summary>Metadados de um modulo no portal.</summary>
public sealed record ModuloInfo(
    string Key,
    string Nome,
    string IconeLucide,
    string Descricao,
    string HrefDefault);
