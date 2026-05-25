using EasyStock.Domain.Entities;
using EasyStock.Domain.ValueObjects;
using FluentAssertions;

namespace EasyStock.Domain.Tests.Sales;

/// <summary>
/// Cobertura do <see cref="Pedido.Total"/> tipado como
/// <see cref="Dinheiro"/> e do <see cref="Pedido.RecalcularTotal"/>.
/// </summary>
public class PedidoTotalTests
{
    [Fact]
    public void Pedido_novo_tem_Total_Zero()
    {
        var pedido = Pedido.Criar(Guid.NewGuid());
        pedido.Total.Should().Be(Dinheiro.Zero);
        pedido.Total.Valor.Should().Be(0m);
    }

    [Fact]
    public void Pedido_novo_Total_nao_e_null()
    {
        // Default property é Dinheiro.Zero, não null. Importante pra evitar
        // NullReferenceException em RecalcularTotal e em mappers que fazem
        // implicit conversion para decimal.
        var pedido = Pedido.Criar(Guid.NewGuid());
        pedido.Total.Should().NotBeNull();
    }

    [Fact]
    public void RecalcularTotal_sem_itens_resulta_em_Zero()
    {
        var pedido = Pedido.Criar(Guid.NewGuid());
        pedido.RecalcularTotal();
        pedido.Total.Should().Be(Dinheiro.Zero);
    }

    [Fact]
    public void RecalcularTotal_soma_subtotais_dos_itens()
    {
        var pedido = Pedido.Criar(Guid.NewGuid());
        pedido.Itens.Add(new PedidoItem { Nome = "Item A", Quantidade = 2, PrecoUnitario = 10m, Subtotal = 20m });
        pedido.Itens.Add(new PedidoItem { Nome = "Item B", Quantidade = 1, PrecoUnitario = 35.5m, Subtotal = 35.5m });

        pedido.RecalcularTotal();

        pedido.Total.Should().Be(Dinheiro.FromDecimal(55.5m));
    }

    [Fact]
    public void RecalcularTotal_atualiza_AlteradoEm()
    {
        var pedido = Pedido.Criar(Guid.NewGuid());
        var antes = pedido.AlteradoEm;
        Thread.Sleep(5); // garantir delta perceptível em UtcNow
        pedido.RecalcularTotal();
        pedido.AlteradoEm.Should().BeAfter(antes);
    }

    [Fact]
    public void Total_implicit_decimal_conversion_funciona()
    {
        // Compat com mappers/DTOs que esperam decimal (ex: PedidoResult.Total).
        var pedido = Pedido.Criar(Guid.NewGuid());
        pedido.Total = Dinheiro.FromDecimal(42.75m);

        decimal asDecimal = pedido.Total;
        asDecimal.Should().Be(42.75m);
    }

    [Fact]
    public void Atribuicao_direta_de_decimal_nao_compila_uso_FromDecimal()
    {
        // Documenta a API do Dinheiro VO: setter aceita Dinheiro, não decimal.
        // Isso é por design — força uso explícito de Dinheiro.FromDecimal pra
        // garantir validação (não negativo) e arredondamento (2 decimais).
        var pedido = Pedido.Criar(Guid.NewGuid());
        pedido.Total = Dinheiro.FromDecimal(99.999m); // arredonda pra 100.00
        pedido.Total.Valor.Should().Be(100.00m);
    }

    [Fact]
    public void RecalcularTotal_com_subtotal_negativo_lanca()
    {
        // PedidoItem.Subtotal teoricamente nunca é negativo, mas se vier por
        // bug, RecalcularTotal sinaliza via Dinheiro.FromDecimal(soma < 0).
        var pedido = Pedido.Criar(Guid.NewGuid());
        pedido.Itens.Add(new PedidoItem { Nome = "Bug", Quantidade = 1, PrecoUnitario = 10m, Subtotal = -50m });

        Action act = () => pedido.RecalcularTotal();
        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
