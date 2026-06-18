using EasyStock.Domain.Entities.Storefront;
using EasyStock.Domain.Exceptions;
using EasyStock.Domain.ValueObjects;
using FluentAssertions;

namespace EasyStock.Domain.Tests.Entities.Storefront;

/// <summary>
/// Testes de <see cref="CardapioItemVariacao"/> (ADR-0035): opção do item guarda-chuva
/// (ex.: "300g" R$28 / "800g" R$42). Cobre factory + invariantes + rótulo preservando caixa.
/// </summary>
public class CardapioItemVariacaoTests
{
    private static readonly Guid ItemId = Guid.NewGuid();

    [Fact]
    public void Criar_define_estado_inicial_seguro()
    {
        var v = CardapioItemVariacao.Criar(ItemId, "300g", 28.00m);

        v.Id.Should().NotBeEmpty();
        v.CardapioItemId.Should().Be(ItemId);
        v.Rotulo.Should().Be("300g");
        v.PrecoStorefront.Should().Be(28.00m);
        v.Disponivel.Should().BeTrue("opção nasce disponível");
        v.EhPadrao.Should().BeFalse("padrão é controlado pelo agregado");
        v.Sku.Should().BeNull();
        v.ProdutoVariacaoId.Should().BeNull();
        v.CriadoEm.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(2));
    }

    [Theory]
    [InlineData("P")]
    [InlineData("G")]
    [InlineData("300g")]
    [InlineData("Família")]
    public void Criar_preserva_a_caixa_do_rotulo(string rotulo)
    {
        var v = CardapioItemVariacao.Criar(ItemId, rotulo, 10m);

        v.Rotulo.Should().Be(rotulo, "o rótulo preserva a caixa; a unicidade é case-insensitive no banco");
    }

    [Fact]
    public void Criar_faz_trim_do_rotulo()
    {
        var v = CardapioItemVariacao.Criar(ItemId, "  800g  ", 42m);
        v.Rotulo.Should().Be("800g");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Criar_rotulo_vazio_throws(string? rotulo)
    {
        var act = () => CardapioItemVariacao.Criar(ItemId, rotulo!, 10m);
        act.Should().Throw<RegraDeDominioVioladaException>().WithMessage("*Rótulo*");
    }

    [Fact]
    public void Criar_rotulo_maior_que_60_throws()
    {
        var act = () => CardapioItemVariacao.Criar(ItemId, new string('x', 61), 10m);
        act.Should().Throw<RegraDeDominioVioladaException>().WithMessage("*Rótulo*60*");
    }

    [Fact]
    public void Criar_preco_negativo_throws()
    {
        var act = () => CardapioItemVariacao.Criar(ItemId, "300g", -0.01m);
        act.Should().Throw<RegraDeDominioVioladaException>().WithMessage("*negativo*");
    }

    [Fact]
    public void Criar_preco_zero_e_permitido()
    {
        // Diferente do CardapioItem avulso (que exige > 0): uma opção pode ser "grátis"
        // (ex.: tamanho cortesia). Dinheiro/coluna aceitam >= 0.
        var v = CardapioItemVariacao.Criar(ItemId, "Degustação", 0m);
        v.PrecoStorefront.Should().Be(0m);
    }

    [Fact]
    public void Criar_arredonda_preco_para_2_casas()
    {
        var v = CardapioItemVariacao.Criar(ItemId, "300g", 28.005m);
        v.PrecoStorefront.Should().Be(28.01m, "arredonda AwayFromZero p/ caber em decimal(10,2)");
    }

    [Fact]
    public void Criar_ordem_negativa_throws()
    {
        var act = () => CardapioItemVariacao.Criar(ItemId, "300g", 28m, ordemExibicao: -1);
        act.Should().Throw<RegraDeDominioVioladaException>().WithMessage("*Ordem*");
    }

    [Fact]
    public void Criar_peso_maior_que_50_throws()
    {
        var act = () => CardapioItemVariacao.Criar(ItemId, "300g", 28m, pesoExibicao: new string('x', 51));
        act.Should().Throw<RegraDeDominioVioladaException>().WithMessage("*Peso*");
    }

    [Fact]
    public void Criar_com_sku_e_variacao_erp()
    {
        var pvId = Guid.NewGuid();
        var v = CardapioItemVariacao.Criar(ItemId, "300g", 28m, sku: CodigoSku.From("rav-300"), produtoVariacaoId: pvId);

        v.Sku!.Value.Should().Be("RAV-300", "CodigoSku normaliza para uppercase");
        v.ProdutoVariacaoId.Should().Be(pvId);
    }

    [Fact]
    public void Atualizar_altera_campos_e_marca_data()
    {
        var v = CardapioItemVariacao.Criar(ItemId, "300g", 28m);
        var antes = v.AlteradoEm;
        Thread.Sleep(10);

        v.Atualizar("800g", 42m, ordemExibicao: 2, pesoExibicao: "800g");

        v.Rotulo.Should().Be("800g");
        v.PrecoStorefront.Should().Be(42m);
        v.OrdemExibicao.Should().Be(2);
        v.PesoExibicao.Should().Be("800g");
        v.AlteradoEm.Should().BeAfter(antes);
    }

    [Fact]
    public void MarcarEsgotado_e_Disponivel()
    {
        var v = CardapioItemVariacao.Criar(ItemId, "300g", 28m);

        v.MarcarEsgotado();
        v.Disponivel.Should().BeFalse();

        v.MarcarDisponivel();
        v.Disponivel.Should().BeTrue();
    }

    [Fact]
    public void DefinirPadrao_alterna_flag()
    {
        var v = CardapioItemVariacao.Criar(ItemId, "300g", 28m);

        v.DefinirPadrao(true);
        v.EhPadrao.Should().BeTrue();

        v.DefinirPadrao(false);
        v.EhPadrao.Should().BeFalse();
    }
}
