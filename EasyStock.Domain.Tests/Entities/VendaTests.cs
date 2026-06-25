using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Domain.ValueObjects;
using FluentAssertions;

namespace EasyStock.Domain.Tests.Entities;

public class VendaTests
{
    [Fact]
    public void Deve_recalcular_valor_total_ao_adicionar_itens()
    {
        var venda = Venda.Criar(
            Guid.NewGuid(),
            Guid.NewGuid(),
            CanalVenda.MercadoLivre,
            NaturezaMovimentacaoEstoque.Venda,
            new DateTime(2026, 4, 3, 0, 0, 0, DateTimeKind.Utc),
            null,
            "NF-1",
            null,
            DateTime.UtcNow);

        venda.AdicionarItem(new ItemVenda
        {
            Id = Guid.NewGuid(),
            Quantidade = Quantidade.From(2),
            PrecoUnitario = Dinheiro.FromDecimal(100m),
            PrecoTotal = Dinheiro.FromDecimal(200m),
            CriadoEm = DateTime.UtcNow,
            Produto = new Produto { Status = StatusProduto.Ativo, Nome = "Item 1" }
        });

        venda.AdicionarItem(new ItemVenda
        {
            Id = Guid.NewGuid(),
            Quantidade = Quantidade.From(1),
            PrecoUnitario = Dinheiro.FromDecimal(50m),
            PrecoTotal = Dinheiro.FromDecimal(50m),
            CriadoEm = DateTime.UtcNow,
            Produto = new Produto { Status = StatusProduto.Ativo, Nome = "Item 2" }
        });

        venda.ValorTotal.Valor.Should().Be(250m);
        venda.ItensVenda.Should().HaveCount(2);
    }

    [Fact]
    public void Subtotal_e_a_soma_bruta_dos_itens_nao_inflado_pelo_desconto()
    {
        var venda = Venda.Criar(
            Guid.NewGuid(), Guid.NewGuid(), CanalVenda.MercadoLivre,
            NaturezaMovimentacaoEstoque.Venda,
            new DateTime(2026, 4, 3, 0, 0, 0, DateTimeKind.Utc),
            null, "NF-1", null, DateTime.UtcNow);

        venda.ValorDesconto = Dinheiro.FromDecimal(10m);
        venda.AdicionarItem(new ItemVenda
        {
            Id = Guid.NewGuid(),
            Quantidade = Quantidade.From(1),
            PrecoUnitario = Dinheiro.FromDecimal(100m),
            PrecoTotal = Dinheiro.FromDecimal(100m),
            CriadoEm = DateTime.UtcNow,
            Produto = new Produto { Status = StatusProduto.Ativo, Nome = "X" }
        });

        // Subtotal = bruto (100), NÃO 110: o bug somava o ValorDesconto ao Subtotal.
        venda.ValorTotal.Valor.Should().Be(100m);
        // Subtotal e sempre preenchido por Criar()/RecalcularValorTotal (Venda.cs:81,106);
        // null-forgive reflete o invariant de runtime sem mascarar NRE real (CI usa -warnaserror).
        venda.Subtotal!.Valor.Should().Be(100m);
    }
}
