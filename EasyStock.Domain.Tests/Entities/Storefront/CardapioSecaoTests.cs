using EasyStock.Domain.Entities.Storefront;
using EasyStock.Domain.Exceptions;
using FluentAssertions;

namespace EasyStock.Domain.Tests.Entities.Storefront;

/// <summary>
/// Testes de <see cref="CardapioSecao"/> (ADR-0035): seção hierárquica do cardápio
/// (≤3 níveis), sem reparent na v1. Cobre factory + profundidade + mutadores.
/// </summary>
public class CardapioSecaoTests
{
    private static readonly Guid StorefrontId = Guid.NewGuid();

    [Fact]
    public void CriarRaiz_nivel_zero_visivel_por_padrao()
    {
        var s = CardapioSecao.CriarRaiz(StorefrontId, "Massas", ordemExibicao: 1);

        s.Id.Should().NotBeEmpty();
        s.StorefrontId.Should().Be(StorefrontId);
        s.SecaoPaiId.Should().BeNull();
        s.Nivel.Should().Be((short)0);
        s.Nome.Should().Be("Massas");
        s.OrdemExibicao.Should().Be(1);
        s.Visivel.Should().BeTrue();
    }

    [Fact]
    public void CriarSubsecao_incrementa_nivel_e_aponta_para_o_pai()
    {
        var depto = CardapioSecao.CriarRaiz(StorefrontId, "Eletrônicos");
        var categoria = CardapioSecao.CriarSubsecao(depto, "Celulares");
        var subcategoria = CardapioSecao.CriarSubsecao(categoria, "Smartphones");

        categoria.Nivel.Should().Be((short)1);
        categoria.SecaoPaiId.Should().Be(depto.Id);
        subcategoria.Nivel.Should().Be((short)2);
        subcategoria.SecaoPaiId.Should().Be(categoria.Id);
        subcategoria.StorefrontId.Should().Be(StorefrontId, "herda o storefront do pai");
    }

    [Fact]
    public void CriarSubsecao_alem_da_profundidade_maxima_throws()
    {
        var depto = CardapioSecao.CriarRaiz(StorefrontId, "Eletrônicos");        // nível 0
        var categoria = CardapioSecao.CriarSubsecao(depto, "Celulares");          // nível 1
        var subcategoria = CardapioSecao.CriarSubsecao(categoria, "Smartphones"); // nível 2

        var act = () => CardapioSecao.CriarSubsecao(subcategoria, "5G"); // nível 3 → proibido

        act.Should().Throw<RegraDeDominioVioladaException>().WithMessage("*Profundidade*");
    }

    [Fact]
    public void CriarSubsecao_pai_null_throws()
    {
        var act = () => CardapioSecao.CriarSubsecao(null!, "Celulares");
        act.Should().Throw<RegraDeDominioVioladaException>().WithMessage("*pai*");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void CriarRaiz_nome_vazio_throws(string? nome)
    {
        var act = () => CardapioSecao.CriarRaiz(StorefrontId, nome!);
        act.Should().Throw<RegraDeDominioVioladaException>().WithMessage("*Nome*");
    }

    [Fact]
    public void CriarRaiz_nome_maior_que_100_throws()
    {
        var act = () => CardapioSecao.CriarRaiz(StorefrontId, new string('x', 101));
        act.Should().Throw<RegraDeDominioVioladaException>().WithMessage("*Nome*100*");
    }

    [Fact]
    public void CriarRaiz_storefront_vazio_throws()
    {
        var act = () => CardapioSecao.CriarRaiz(Guid.Empty, "Massas");
        act.Should().Throw<RegraDeDominioVioladaException>().WithMessage("*Storefront*");
    }

    [Fact]
    public void Renomear_altera_nome_e_data()
    {
        var s = CardapioSecao.CriarRaiz(StorefrontId, "Massas");
        var antes = s.AlteradoEm;
        Thread.Sleep(10);

        s.Renomear("Massas Frescas");

        s.Nome.Should().Be("Massas Frescas");
        s.AlteradoEm.Should().BeAfter(antes);
    }

    [Fact]
    public void Reordenar_negativo_throws()
    {
        var s = CardapioSecao.CriarRaiz(StorefrontId, "Massas");
        var act = () => s.Reordenar(-1);
        act.Should().Throw<RegraDeDominioVioladaException>();
    }

    [Fact]
    public void AlterarVisibilidade_alterna()
    {
        var s = CardapioSecao.CriarRaiz(StorefrontId, "Massas");

        s.AlterarVisibilidade(false);
        s.Visivel.Should().BeFalse();

        s.AlterarVisibilidade(true);
        s.Visivel.Should().BeTrue();
    }
}
