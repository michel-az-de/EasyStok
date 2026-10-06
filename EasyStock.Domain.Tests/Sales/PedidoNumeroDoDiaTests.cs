using EasyStock.Domain.Entities;
using EasyStock.Domain.Exceptions;
using EasyStock.Domain.Entities.Storefront;
using EasyStock.Domain.Enums.Storefront;
using FluentAssertions;

namespace EasyStock.Domain.Tests.Sales;

/// <summary>S53 (#1283): número do dia do pedido e conservação do item do cardápio.</summary>
public class PedidoNumeroDoDiaTests
{
    private static readonly DateOnly Dia = new(2026, 10, 2);

    [Fact]
    public void DefinirGravaNumeroEDia()
    {
        var pedido = Pedido.Criar(Guid.NewGuid());

        pedido.DefinirNumeroDoDia(Dia, 42);

        pedido.NumeroDoDia.Should().Be(42);
        pedido.DataNumero.Should().Be(Dia);
        pedido.TemNumeroDoDia(Dia).Should().BeTrue();
        pedido.TemNumeroDoDia(Dia.AddDays(1)).Should().BeFalse("dia novo pede número novo");
    }

    [Fact]
    public void DiaNovoTrocaONumero()
    {
        var pedido = Pedido.Criar(Guid.NewGuid());
        pedido.DefinirNumeroDoDia(Dia, 42);

        pedido.DefinirNumeroDoDia(Dia.AddDays(1), 3);

        pedido.NumeroDoDia.Should().Be(3);
        pedido.DataNumero.Should().Be(Dia.AddDays(1));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void NumeroPrecisaSerPositivo(int numero)
    {
        var act = () => Pedido.Criar(Guid.NewGuid()).DefinirNumeroDoDia(Dia, numero);
        act.Should().Throw<RegraDeDominioVioladaException>();
    }

    [Fact]
    public void ConservacaoDoItemDoCardapio()
    {
        var item = CardapioItem.CriarAvulso(Guid.NewGuid(), "Nhoque", 30m);
        item.Conservacao.Should().Be(ConservacaoProduto.Ambiente);

        item.DefinirConservacao(ConservacaoProduto.Congelado);
        item.Conservacao.Should().Be(ConservacaoProduto.Congelado);
        ConservacaoProduto.Congelado.ParaContrato().Should().Be("congelado");

        var act = () => item.DefinirConservacao((ConservacaoProduto)9);
        act.Should().Throw<RegraDeDominioVioladaException>();
    }
}
