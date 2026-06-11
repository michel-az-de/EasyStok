using EasyStock.Web.Navigation;
using FluentAssertions;

namespace EasyStock.Web.UnitTests.Navigation;

/// <summary>
/// Garante a estrutura canonica do menu (ADR-0032) e — critico — que TODO valor de
/// <c>ViewBag.ActiveMenuItem</c> emitido pelos controllers resolve para um item
/// (alias orfao = pagina perde estado ativo silenciosamente). O snapshot abaixo veio
/// do grep dos controllers do EasyStock.Web na fatia 1; se um controller novo emitir
/// um alias, adicione-o aqui E ao ActiveKeys do item correspondente.
/// </summary>
public class MenuDefinitionTests
{
    // Inventario (grep de ActiveMenuItem em EasyStock.Web/Controllers, fatia 1).
    private static readonly string[] AliasesEmitidosPelosControllers =
    {
        "Dashboard", "Produtos", "ProdutosMobile", "Estoque", "Fornecedores",
        "Clientes", "ClientesMobile", "Pedidos", "PedidosMobile", "Caixa",
        "CaixaMobile", "Financeiro", "ContasAReceber", "ContasAPagar", "Lotes",
        "LotesMobile", "ListasCompras", "Categorias", "Entradas", "Saidas",
        "NotasFiscais", "ConfiguracaoFiscal", "Kds", "Dispositivos", "Operacao",
        "Analytics", "Movimentacoes", "Inteligencia", "InteligenciaLojas",
        "Relatorios", "Anuncios", "Usuarios", "Configuracoes", "Lojas",
        "Assinatura", "Notificacoes", "Preferencias",
    };

    [Fact]
    public void Grupos_na_ordem_e_chaves_da_IA()
    {
        MenuDefinition.Groups.Select(g => g.Key).Should().Equal(
            "operacao", "producao-estoque", "compras", "financeiro", "crescimento");
    }

    [Fact]
    public void Rodape_tem_dispositivos_usuarios_configuracoes()
    {
        MenuDefinition.Footer.Select(i => i.Key).Should().Equal(
            "dispositivos", "usuarios", "configuracoes");
    }

    [Fact]
    public void Operacao_contem_pedidos_kds_visor_caixa_clientes()
    {
        var operacao = MenuDefinition.Groups.Single(g => g.Key == "operacao");
        operacao.Items.Select(i => i.Key).Should().Equal(
            "pedidos", "kds-operacao", "kds-visor", "caixa", "clientes");
    }

    [Fact]
    public void Todo_alias_emitido_resolve_para_um_item()
    {
        // kdsHabilitado=true garante que itens de producao (ex.: alias "Kds") estejam visiveis.
        foreach (var alias in AliasesEmitidosPelosControllers)
        {
            var vm = MenuViewModelBuilder.Build(
                currentPath: null, activeMenuItem: alias,
                favoritosKeys: null, badges: null, kdsHabilitado: true);

            vm.ActiveKey.Should().NotBeNull($"o alias '{alias}' precisa ter dono em algum ActiveKeys");
        }
    }

    [Fact]
    public void Nenhum_alias_pertence_a_dois_itens()
    {
        var donoPorAlias = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in MenuDefinition.AllItems())
            foreach (var alias in item.ActiveKeys)
            {
                donoPorAlias.ContainsKey(alias).Should().BeFalse(
                    $"alias '{alias}' duplicado entre itens (ja em '{donoPorAlias.GetValueOrDefault(alias)}', tambem em '{item.Key}')");
                donoPorAlias[alias] = item.Key;
            }
    }

    [Fact]
    public void Chaves_de_item_sao_unicas()
    {
        var keys = MenuDefinition.AllItems().Select(i => i.Key).ToList();
        keys.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Seed_loja_com_kds_traz_fluxo_de_producao()
    {
        MenuDefinition.DefaultFavoritos(kdsHabilitado: true).Should().Equal(
            "pedidos", "kds-operacao", "lotes-validade", "posicao-estoque");
    }

    [Fact]
    public void Seed_loja_sem_kds_traz_minimo()
    {
        MenuDefinition.DefaultFavoritos(kdsHabilitado: false).Should().Equal(
            "pedidos", "posicao-estoque");
    }

    [Fact]
    public void Badges_e_tag_estao_nos_itens_certos()
    {
        var byKey = MenuDefinition.AllItems().ToDictionary(i => i.Key);

        byKey["pedidos"].BadgeKey.Should().Be(MenuDefinition.BadgePedidosAbertos);
        byKey["lotes-validade"].BadgeKey.Should().Be(MenuDefinition.BadgeLotesVencidos);
        byKey["posicao-estoque"].BadgeKey.Should().Be(MenuDefinition.BadgeProdutosCriticos);
        byKey["dashboard"].BadgeKey.Should().Be(MenuDefinition.BadgeDashboardTotal);
        byKey["kds-visor"].Tag.Should().Be("alpha");
        byKey["kds-visor"].IsExternal.Should().BeTrue();
        byKey["kds-operacao"].IsProducaoKds.Should().BeTrue();
    }
}
