using EasyStock.Domain.Entities;
using FluentAssertions;

namespace EasyStock.Domain.Tests.Entities;

/// <summary>
/// #2018 (estrutura-17, comanda-12): uma definição de "linha de frete" e uma fórmula de total. Antes, nove pontos
/// reescreviam o predicado do frete de jeitos diferentes e quatro somavam o total.
/// </summary>
public class PedidoFreteETotalTests
{
    private static PedidoItem Linha(string nome, decimal subtotal, Guid? cardapio = null, Guid? produto = null, string? linha = null) => new()
    {
        Id = Guid.NewGuid(), Nome = nome, Quantidade = 1, PrecoUnitario = subtotal, Subtotal = subtotal,
        CardapioItemId = cardapio, ProdutoId = produto, LinhaSnapshot = linha,
    };

    private static Pedido ComLinhas(params PedidoItem[] linhas)
    {
        var pedido = Pedido.Criar(Guid.NewGuid());
        foreach (var l in linhas) pedido.Itens.Add(l);
        return pedido;
    }

    [Theory]
    [InlineData("Entrega — Centro", false, false, null, true)]
    [InlineData("Entrega — Centro", true, false, null, false)]
    [InlineData("Entrega — Centro", false, true, null, false)]
    [InlineData("Entrega — Centro", false, false, "paraServir", false)]
    [InlineData("Entrega de bolos", false, false, null, false)]
    [InlineData("Taxa extra", false, false, null, false)]
    public void LinhaDeFrete_ESoAQueOCheckoutGravaSemPratoNemProduto(string nome, bool comCardapio, bool comProduto, string? snapshot, bool esperado)
    {
        var item = Linha(nome, 8m, comCardapio ? Guid.NewGuid() : null, comProduto ? Guid.NewGuid() : null, snapshot);

        item.EhLinhaDeFrete.Should().Be(esperado);
        PedidoItem.EhFrete(item.CardapioItemId, item.ProdutoId, item.LinhaSnapshot, item.Nome).Should().Be(esperado);
    }

    [Fact]
    public void LinhaProduto_EPratoOuProdutoOuQuemTemLinhaDePreparo()
    {
        Linha("Lasanha", 25m, cardapio: Guid.NewGuid()).EhProduto.Should().BeTrue();
        Linha("Embalagem", 4m, produto: Guid.NewGuid()).EhProduto.Should().BeTrue();
        Linha("Antigo", 4m, linha: "prepararEmCasa").EhProduto.Should().BeTrue();
        Linha("Entrega — Centro", 8m).EhProduto.Should().BeFalse();
        Linha("Taxa extra", 3m).EhProduto.Should().BeFalse();
    }

    [Fact]
    public void FreteEForaDoCardapio_SeparamOFreteDaLinhaAvulsaDaCasa()
    {
        var pedido = ComLinhas(
            Linha("Lasanha", 25m, cardapio: Guid.NewGuid()),
            Linha("Embalagem térmica", 4m, produto: Guid.NewGuid()),
            Linha("Taxa extra", 3m),
            Linha("Entrega — Centro", 8m));

        pedido.Frete.Should().Be(8m, "só a linha do checkout é frete; a avulsa da casa não infla o frete");
        pedido.ValorForaDoCardapio.Should().Be(15m, "frete 8 + produto da casa 4 + avulsa 3");
        pedido.TotalComPratos(40m).Should().Be(55m);
    }

    [Fact]
    public void RecalcularTotal_SomaTodasAsLinhasInclusiveOFrete()
    {
        var pedido = ComLinhas(Linha("Lasanha", 25m, cardapio: Guid.NewGuid()), Linha("Entrega — Centro", 8m));

        pedido.RecalcularTotal();

        pedido.Total.Valor.Should().Be(33m);
    }

    [Fact]
    public void TotalDe_EAFormulaUnica_ESinalizaSubtotalNegativo()
    {
        Pedido.TotalDe([25m, 8m]).Valor.Should().Be(33m);
        Pedido.TotalDe([]).Valor.Should().Be(0m);

        var negativo = () => Pedido.TotalDe([-1m]);
        negativo.Should().Throw<ArgumentOutOfRangeException>();
    }
}
