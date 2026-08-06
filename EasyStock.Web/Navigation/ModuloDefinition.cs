namespace EasyStock.Web.Navigation;

/// <summary>
/// Definição estática dos módulos do shell modular por empresa (tenant).
/// Cada módulo mapeia para um grupo do MenuDefinition.
/// </summary>
public static class ModuloDefinition
{
    /// <summary>Mapeia chave do módulo → chave do grupo no MenuDefinition.</summary>
    public static string? GrupoDoModulo(string moduloKey) => moduloKey switch
    {
        "operacao" => "operacao",
        "producao" => "producao-estoque",
        "compras" => "compras",
        "financeiro" => "financeiro",
        "crescimento" => "crescimento",
        "admin" => null, // rodapé, não grupo
        "comercial" => null, // FMA: não existe ainda no MenuDefinition
        "crm" => null, // FMA: não existe ainda
        _ => null
    };

    /// <summary>Módulos disponíveis para a Casa da Babá.</summary>
    public static IReadOnlyList<ModuloInfo> ModulosCasaDaBaba { get; } =
    [
        new("operacao", "Operação", "activity", "Pedidos, KDS, caixa, clientes e cardápio", "/pedidos", "mod-op"),
        new("producao", "Produção e Estoque", "boxes", "Validade, posição, entradas, saídas e produtos", "/estoque?status=vencido", "mod-prod"),
        new("compras", "Compras", "shopping-bag", "Pedidos de compra e fornecedores", "/listas-compras", "mod-comp"),
        new("financeiro", "Financeiro", "landmark", "Contas a receber, pagar e notas fiscais", "/financeiro", "mod-fin"),
        new("crescimento", "Crescimento", "trending-up", "Análises, relatórios e anúncios", "/analytics", "mod-cre"),
        new("admin", "Administração", "settings", "Dispositivos, usuários e configurações", "/configuracoes", "mod-adm"),
    ];

    /// <summary>Módulos disponíveis para a FMA Informática.</summary>
    public static IReadOnlyList<ModuloInfo> ModulosFma { get; } =
    [
        new("comercial", "Comercial", "handshake", "Propostas, contratos e catálogo de serviços", "/propostas", "mod-op"),
        new("crm", "CRM", "users", "Clientes, pipeline e oportunidades", "/clientes", "mod-cre"),
        new("financeiro", "Financeiro", "landmark", "Contas a receber, pagar e notas fiscais", "/financeiro", "mod-fin"),
        new("admin", "Administração", "settings", "Usuários e configurações", "/configuracoes", "mod-adm"),
    ];

    /// <summary>Retorna os módulos do tenant atual.</summary>
    public static IReadOnlyList<ModuloInfo> PorEmpresa(string empresaId) =>
        empresaId.Equals("fma", StringComparison.OrdinalIgnoreCase)
            ? ModulosFma
            : ModulosCasaDaBaba;
}

/// <summary>Metadados de um módulo no launcher.</summary>
public sealed record ModuloInfo(
    string Key,
    string Nome,
    string IconeLucide,
    string Descricao,
    string HrefDefault,
    string CorClasse);
