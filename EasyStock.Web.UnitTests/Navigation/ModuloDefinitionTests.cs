using EasyStock.Web.Navigation;
using FluentAssertions;

namespace EasyStock.Web.UnitTests.Navigation;

/// <summary>
/// Guarda do shell modular (ADR-0046). O <see cref="MenuDefinition"/> continua sendo a
/// fonte unica da estrutura (ADR-0032) e o <see cref="ModuloDefinition"/> so mapeia
/// modulo -> grupo; estes testes amarram os dois para que renomear a key de um grupo
/// nao orfanize um modulo em silencio — o mesmo risco que o ADR-0032 ja registra para
/// favoritos. Cobrem tambem o resolver por rota, que substituiu a querystring.
/// </summary>
public class ModuloDefinitionTests
{
    [Fact]
    public void Todo_modulo_nao_admin_aponta_para_um_grupo_existente()
    {
        var chavesDeGrupo = MenuDefinition.Groups.Select(g => g.Key).ToHashSet(StringComparer.Ordinal);

        foreach (var modulo in ModuloDefinition.Modulos.Where(m => m.Key != ModuloDefinition.ModuloAdmin))
        {
            var grupo = ModuloDefinition.GrupoDoModulo(modulo.Key);
            grupo.Should().NotBeNull(because: $"o modulo '{modulo.Key}' precisa mapear para um grupo");
            chavesDeGrupo.Should().Contain(grupo!, because: $"o grupo de '{modulo.Key}' precisa existir no MenuDefinition");
        }
    }

    [Fact]
    public void Todo_grupo_do_menu_tem_um_modulo_dono()
    {
        // Sem isto, um grupo novo ficaria inalcancavel pelo portal.
        foreach (var grupo in MenuDefinition.Groups)
            ModuloDefinition.ModuloDoGrupo(grupo.Key).Should().NotBeNull(
                because: $"o grupo '{grupo.Key}' precisa aparecer como modulo no portal");
    }

    [Fact]
    public void Mapeamento_modulo_grupo_e_reversivel()
    {
        foreach (var modulo in ModuloDefinition.Modulos.Where(m => m.Key != ModuloDefinition.ModuloAdmin))
            ModuloDefinition.ModuloDoGrupo(ModuloDefinition.GrupoDoModulo(modulo.Key)).Should().Be(modulo.Key);
    }

    [Fact]
    public void Admin_nao_e_grupo_e_sim_o_rodape()
    {
        ModuloDefinition.GrupoDoModulo(ModuloDefinition.ModuloAdmin).Should().BeNull();
        ModuloDefinition.PorChave(ModuloDefinition.ModuloAdmin).Should().NotBeNull();
    }

    [Fact]
    public void Href_padrao_de_cada_modulo_leva_ao_proprio_modulo()
    {
        // Trava anti-drift: mudar um HrefDefault para uma rota de outro grupo faria o
        // card do portal abrir um modulo e o menu mostrar outro.
        foreach (var modulo in ModuloDefinition.Modulos)
            ModuloDefinition.ResolverPorRota(modulo.HrefDefault).Should().Be(modulo.Key,
                because: $"o card '{modulo.Key}' aponta para {modulo.HrefDefault}");
    }

    [Theory]
    [InlineData("/pedidos", "operacao")]
    [InlineData("/caixa", "operacao")]
    [InlineData("/estoque", "producao")]
    [InlineData("/estoque?status=vencido", "producao")]
    [InlineData("/entradas/historico", "producao")]
    [InlineData("/listas-compras", "compras")]
    [InlineData("/contas-a-receber", "financeiro")]
    [InlineData("/analytics", "crescimento")]
    [InlineData("/usuarios", "admin")]
    [InlineData("/configuracoes", "admin")]
    public void Rota_resolve_o_modulo(string path, string esperado)
    {
        ModuloDefinition.ResolverPorRota(path).Should().Be(esperado);
    }

    [Theory]
    [InlineData("/dashboard")]
    [InlineData("/launcher")]
    [InlineData("/")]
    [InlineData("/rota/que/nao/existe")]
    [InlineData(null)]
    public void Rota_sem_dono_nao_tem_modulo(string? path)
    {
        // Menu inteiro (fail-open). O Dashboard e ancora: nao pertence a modulo nenhum.
        ModuloDefinition.ResolverPorRota(path).Should().BeNull();
    }

    [Theory]
    [InlineData("/operacao")]
    [InlineData("/preferencias")]
    [InlineData("/lojas")]
    [InlineData("/assinatura")]
    public void Rota_que_so_casa_por_alias_legado_nao_define_modulo(string path)
    {
        // Os aliases de ActiveMenuItem servem para DESTACAR um item parecido, nao para
        // declarar area. /operacao emite ActiveMenuItem="Operacao", que pertence ao item de
        // rodape "Dispositivos": resolver por ele esconderia TODOS os grupos numa tela de
        // operacao, deixando o usuario sem Pedidos, Caixa, Estoque nem Financeiro.
        ModuloDefinition.ResolverPorRota(path).Should().BeNull();
    }

    [Fact]
    public void Portal_expoe_um_card_por_grupo_mais_a_administracao()
    {
        ModuloDefinition.Modulos.Should().HaveCount(MenuDefinition.Groups.Count + 1);
        ModuloDefinition.Modulos.Select(m => m.Key).Should().OnlyHaveUniqueItems();
    }
}
