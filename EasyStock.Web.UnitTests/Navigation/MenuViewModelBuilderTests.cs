using EasyStock.Web.Navigation;
using FluentAssertions;

namespace EasyStock.Web.UnitTests.Navigation;

/// <summary>
/// Logica pura do menu (ADR-0032): ativo-por-rota (segmentos), soma de badges,
/// favoritos (dedup/cap/orfaos) e filtro pela flag KDS. Sem HttpContext.
/// </summary>
public class MenuViewModelBuilderTests
{
    private static MenuViewModel Build(
        string? path = null, string? active = null,
        IReadOnlyList<string>? favoritos = null, MenuBadges? badges = null,
        bool kds = true) =>
        MenuViewModelBuilder.Build(path, active, favoritos, badges, kds);

    // ── ativo-por-rota ───────────────────────────────────────────────

    [Theory]
    [InlineData("/pedidos", "pedidos")]
    [InlineData("/estoque", "posicao-estoque")]
    [InlineData("/configuracoes", "configuracoes")]
    [InlineData("/kds", "kds-operacao")]
    public void Rota_exata_ativa_o_item(string path, string esperado)
    {
        Build(path: path).ActiveKey.Should().Be(esperado);
    }

    [Fact]
    public void Rota_de_dois_segmentos_ativa_o_item()
    {
        Build(path: "/entradas/historico").ActiveKey.Should().Be("entradas");
        Build(path: "/saidas/historico").ActiveKey.Should().Be("saidas");
    }

    [Fact]
    public void Trailing_slash_e_ignorado()
    {
        Build(path: "/pedidos/").ActiveKey.Should().Be("pedidos");
    }

    [Fact]
    public void Case_e_ignorado()
    {
        Build(path: "/PEDIDOS").ActiveKey.Should().Be("pedidos");
    }

    [Fact]
    public void Querystring_e_ignorada()
    {
        Build(path: "/estoque?item=abc-123").ActiveKey.Should().Be("posicao-estoque");
    }

    [Fact]
    public void Matching_e_por_segmento_nao_por_prefixo_de_string()
    {
        // /pedidos NAO pode casar uma rota irma tipo /pedidos-kds (inexistente, sem fallback).
        Build(path: "/pedidos-kds").ActiveKey.Should().BeNull();
    }

    [Fact]
    public void Sem_casamento_e_sem_alias_nada_fica_ativo()
    {
        var vm = Build(path: "/rota-que-nao-existe");
        vm.ActiveKey.Should().BeNull();
        vm.Groups.Should().OnlyContain(g => !g.IsOpen);
    }

    [Fact]
    public void Sem_href_casando_faz_fallback_para_active_menu_item()
    {
        // /entradas/nova nao e href de menu; o controller seta ActiveMenuItem="Entradas".
        var vm = Build(path: "/entradas/nova", active: "Entradas");
        vm.ActiveKey.Should().Be("entradas");
        vm.Groups.Single(g => g.Group.Key == "producao-estoque").IsOpen.Should().BeTrue();
    }

    [Fact]
    public void Rota_vence_o_active_menu_item_quando_ambos_existem()
    {
        // href casa /pedidos; o ActiveMenuItem divergente nao deve prevalecer.
        Build(path: "/pedidos", active: "Estoque").ActiveKey.Should().Be("pedidos");
    }

    [Fact]
    public void Grupo_da_rota_ativa_abre_exclusivamente()
    {
        var vm = Build(path: "/caixa");
        vm.Groups.Single(g => g.Group.Key == "operacao").IsOpen.Should().BeTrue();
        vm.Groups.Where(g => g.Group.Key != "operacao").Should().OnlyContain(g => !g.IsOpen);
    }

    // ── badges ───────────────────────────────────────────────────────

    [Fact]
    public void Soma_de_badges_por_grupo()
    {
        var vm = Build(badges: new MenuBadges(PedidosAbertos: 3, ProdutosCriticos: 2, LotesVencidos: 5));

        vm.Groups.Single(g => g.Group.Key == "operacao").BadgeSum.Should().Be(3);          // so pedidos
        vm.Groups.Single(g => g.Group.Key == "producao-estoque").BadgeSum.Should().Be(7);  // lotes 5 + estoque 2
        vm.Groups.Single(g => g.Group.Key == "compras").BadgeSum.Should().Be(0);
    }

    [Fact]
    public void Dashboard_soma_criticos_e_vencidos_sem_pedidos()
    {
        var vm = Build(badges: new MenuBadges(PedidosAbertos: 9, ProdutosCriticos: 2, LotesVencidos: 5));
        vm.Dashboard.Badge.Should().Be(7);
    }

    [Fact]
    public void Sem_badges_tudo_zero()
    {
        var vm = Build();
        vm.Dashboard.Badge.Should().Be(0);
        vm.Groups.Should().OnlyContain(g => g.BadgeSum == 0);
    }

    // ── favoritos (Meu dia) ──────────────────────────────────────────

    [Fact]
    public void Favoritos_preservam_ordem_e_deduplicam()
    {
        var vm = Build(favoritos: new[] { "posicao-estoque", "pedidos", "posicao-estoque" });
        vm.MeuDia.Select(v => v.Key).Should().Equal("posicao-estoque", "pedidos");
    }

    [Fact]
    public void Favorito_com_chave_inexistente_e_descartado()
    {
        var vm = Build(favoritos: new[] { "pedidos", "nao-existe" });
        vm.MeuDia.Select(v => v.Key).Should().Equal("pedidos");
    }

    [Fact]
    public void Favorito_externo_e_descartado()
    {
        // kds-visor abre o PWA em outra aba — nao e um atalho fixavel.
        var vm = Build(favoritos: new[] { "kds-visor" }, kds: true);
        vm.MeuDia.Should().BeEmpty();
    }

    [Fact]
    public void Favoritos_respeitam_cap_de_20()
    {
        var muitos = MenuDefinition.AllItems()
            .Where(i => !i.IsExternal)
            .Select(i => i.Key)
            .Take(21)
            .ToList();
        muitos.Should().HaveCount(21);

        Build(favoritos: muitos).MeuDia.Should().HaveCount(MenuViewModelBuilder.MaxFavoritos);
    }

    [Fact]
    public void Sem_favoritos_meu_dia_vazio()
    {
        Build(favoritos: null).MeuDia.Should().BeEmpty();
        Build(favoritos: Array.Empty<string>()).MeuDia.Should().BeEmpty();
    }

    [Fact]
    public void Item_favoritado_continua_no_grupo_de_origem()
    {
        var vm = Build(favoritos: new[] { "pedidos" });
        vm.MeuDia.Select(v => v.Key).Should().Contain("pedidos");
        vm.Groups.Single(g => g.Group.Key == "operacao")
            .Items.Select(v => v.Key).Should().Contain("pedidos");
    }

    // ── filtro pela flag KDS (P1-6) ──────────────────────────────────

    [Fact]
    public void Flag_kds_off_remove_itens_de_producao_dos_grupos()
    {
        var operacao = Build(kds: false).Groups.Single(g => g.Group.Key == "operacao");
        operacao.Items.Select(i => i.Key).Should().Equal("pedidos", "caixa", "clientes");
    }

    [Fact]
    public void Flag_kds_on_mantem_itens_de_producao()
    {
        var operacao = Build(kds: true).Groups.Single(g => g.Group.Key == "operacao");
        operacao.Items.Select(i => i.Key).Should().Contain("kds-operacao");
    }

    [Fact]
    public void Favorito_de_kds_com_flag_off_nao_renderiza_em_lugar_nenhum()
    {
        var vm = Build(favoritos: new[] { "kds-operacao" }, kds: false);

        vm.MeuDia.Should().BeEmpty();
        vm.Groups.SelectMany(g => g.Items).Select(i => i.Key)
            .Should().NotContain("kds-operacao");
    }
}
