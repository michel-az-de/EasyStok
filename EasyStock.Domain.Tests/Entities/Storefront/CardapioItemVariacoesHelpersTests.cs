using EasyStock.Domain.Entities.Storefront;
using EasyStock.Domain.Exceptions;
using FluentAssertions;

namespace EasyStock.Domain.Tests.Entities.Storefront;

/// <summary>
/// Testes dos helpers de item guarda-chuva em <see cref="CardapioItem"/> (ADR-0035):
/// <c>TemVariacoes</c>, <c>PrecoAPartirDe</c>, <c>VariacaoPadrao</c>, <c>TemDisponibilidade</c>,
/// <c>DefinirVariacaoPadrao</c> e a resolução de <c>CategoriaEfetiva</c> por seção.
/// </summary>
public class CardapioItemVariacoesHelpersTests
{
    private static CardapioItem NovoItemAvulso(decimal preco = 35m)
        => CardapioItem.CriarAvulso(Guid.NewGuid(), "Ravioli de Abóbora", preco, categoria: "massas");

    private static CardapioItemVariacao NovaVariacao(CardapioItem item, string rotulo, decimal preco, double ordem = 0)
        => CardapioItemVariacao.Criar(item.Id, rotulo, preco, ordemExibicao: ordem);

    // ── Back-compat: item sem opções ──────────────────────────────────

    [Fact]
    public void ItemSemVariacoes_comporta_se_como_preco_unico()
    {
        var item = NovoItemAvulso(preco: 35m);

        item.TemVariacoes().Should().BeFalse();
        item.VariacaoPadrao().Should().BeNull();
        item.PrecoAPartirDe().Should().Be(35m, "sem opções, usa PrecoEfetivo (legado)");
        item.TemDisponibilidade().Should().BeTrue();
    }

    // ── Preço "a partir de" = mais barata DISPONÍVEL ──────────────────

    [Fact]
    public void PrecoAPartirDe_retorna_a_opcao_mais_barata_disponivel()
    {
        var item = NovoItemAvulso();
        item.AdicionarVariacao(NovaVariacao(item, "300g", 28m, ordem: 1));
        item.AdicionarVariacao(NovaVariacao(item, "800g", 42m, ordem: 2));

        item.TemVariacoes().Should().BeTrue();
        item.PrecoAPartirDe().Should().Be(28m);
        item.VariacaoPadrao()!.Rotulo.Should().Be("300g");
    }

    [Fact]
    public void PrecoAPartirDe_ignora_opcao_esgotada()
    {
        var item = NovoItemAvulso();
        var p300 = NovaVariacao(item, "300g", 28m, ordem: 1);
        var p800 = NovaVariacao(item, "800g", 42m, ordem: 2);
        item.AdicionarVariacao(p300);
        item.AdicionarVariacao(p800);
        p300.MarcarEsgotado();

        item.PrecoAPartirDe().Should().Be(42m, "a mais barata (300g) está esgotada → usa a 800g");
        item.VariacaoPadrao()!.Rotulo.Should().Be("800g");
    }

    [Fact]
    public void VariacaoPadrao_todas_esgotadas_retorna_a_mais_barata_absoluta()
    {
        var item = NovoItemAvulso();
        var p300 = NovaVariacao(item, "300g", 28m, ordem: 1);
        var p800 = NovaVariacao(item, "800g", 42m, ordem: 2);
        item.AdicionarVariacao(p300);
        item.AdicionarVariacao(p800);
        p300.MarcarEsgotado();
        p800.MarcarEsgotado();

        item.VariacaoPadrao()!.Rotulo.Should().Be("300g", "nenhuma disponível → cai p/ a mais barata absoluta");
        item.PrecoAPartirDe().Should().Be(28m);
        item.TemDisponibilidade().Should().BeFalse("todas as opções esgotadas");
    }

    [Fact]
    public void VariacaoPadrao_respeita_EhPadrao_quando_disponivel()
    {
        var item = NovoItemAvulso();
        var p300 = NovaVariacao(item, "300g", 28m, ordem: 1);
        var p800 = NovaVariacao(item, "800g", 42m, ordem: 2);
        item.AdicionarVariacao(p300);
        item.AdicionarVariacao(p800);

        item.DefinirVariacaoPadrao(p800.Id);

        item.VariacaoPadrao()!.Rotulo.Should().Be("800g", "EhPadrao disponível tem prioridade sobre a mais barata");
        item.PrecoAPartirDe().Should().Be(42m);
    }

    // ── Invariante: ≤1 padrão por item ─────────────────────────────────

    [Fact]
    public void DefinirVariacaoPadrao_limpa_o_padrao_das_irmas()
    {
        var item = NovoItemAvulso();
        var p300 = NovaVariacao(item, "300g", 28m, ordem: 1);
        var p800 = NovaVariacao(item, "800g", 42m, ordem: 2);
        item.AdicionarVariacao(p300);
        item.AdicionarVariacao(p800);

        item.DefinirVariacaoPadrao(p300.Id);
        item.DefinirVariacaoPadrao(p800.Id);

        item.Variacoes.Count(v => v.EhPadrao).Should().Be(1, "no máximo uma opção padrão por item");
        p800.EhPadrao.Should().BeTrue();
        p300.EhPadrao.Should().BeFalse();
    }

    [Fact]
    public void AdicionarVariacao_com_padrao_zera_o_padrao_das_existentes()
    {
        var item = NovoItemAvulso();
        var p300 = NovaVariacao(item, "300g", 28m, ordem: 1);
        item.AdicionarVariacao(p300);
        item.DefinirVariacaoPadrao(p300.Id);

        var p800 = CardapioItemVariacao.Criar(item.Id, "800g", 42m, ehPadrao: true);
        item.AdicionarVariacao(p800);

        item.Variacoes.Count(v => v.EhPadrao).Should().Be(1);
        p800.EhPadrao.Should().BeTrue();
        p300.EhPadrao.Should().BeFalse();
    }

    [Fact]
    public void DefinirVariacaoPadrao_variacao_inexistente_throws()
    {
        var item = NovoItemAvulso();
        item.AdicionarVariacao(NovaVariacao(item, "300g", 28m));

        var act = () => item.DefinirVariacaoPadrao(Guid.NewGuid());
        act.Should().Throw<RegraDeDominioVioladaException>().WithMessage("*não pertence*");
    }

    [Fact]
    public void RemoverVariacao_remove_a_opcao()
    {
        var item = NovoItemAvulso();
        var p300 = NovaVariacao(item, "300g", 28m);
        item.AdicionarVariacao(p300);
        item.AdicionarVariacao(NovaVariacao(item, "800g", 42m));

        item.RemoverVariacao(p300.Id);

        item.Variacoes.Should().HaveCount(1);
        item.Variacoes.Should().NotContain(v => v.Rotulo == "300g");
    }

    // ── CategoriaEfetiva por seção (ADR-0035) ─────────────────────────

    [Fact]
    public void CategoriaEfetiva_usa_o_nome_da_secao_quando_associada()
    {
        var item = NovoItemAvulso(); // CategoriaTexto = "massas"
        item.Secao = CardapioSecao.CriarRaiz(item.StorefrontId, "Massas Frescas");

        item.CategoriaEfetiva().Should().Be("Massas Frescas", "Secao tem prioridade sobre CategoriaTexto");
    }

    [Fact]
    public void CategoriaEfetiva_sem_secao_mantem_comportamento_anterior()
    {
        var item = NovoItemAvulso(); // CategoriaTexto = "massas", sem Secao

        item.CategoriaEfetiva().Should().Be("massas");
    }

    [Fact]
    public void DefinirSecao_atualiza_o_vinculo_de_secao()
    {
        var item = NovoItemAvulso();
        var secaoId = Guid.NewGuid();

        item.DefinirSecao(secaoId);
        item.SecaoId.Should().Be(secaoId);

        item.DefinirSecao(null);
        item.SecaoId.Should().BeNull();
    }
}
