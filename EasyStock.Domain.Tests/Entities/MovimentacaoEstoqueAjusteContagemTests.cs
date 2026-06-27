using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Domain.Exceptions;
using EasyStock.Domain.ValueObjects;
using FluentAssertions;

namespace EasyStock.Domain.Tests.Entities;

public class MovimentacaoEstoqueAjusteContagemTests
{
    private static readonly DateTime Quando = new(2026, 6, 27, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Delta_positivo_vira_entrada_com_qtd_absoluta_e_documento_da_contagem()
    {
        var item = CriarItem();
        var contagemId = Guid.NewGuid();

        var mov = MovimentacaoEstoque.CriarAjusteContagem(
            Guid.NewGuid(), item.EmpresaId, item, delta: 4m,
            Dinheiro.FromDecimal(10m), Quando, contagemId, null, Quando);

        mov.Tipo.Should().Be(TipoMovimentacaoEstoque.Entrada);
        mov.Natureza.Should().Be(NaturezaMovimentacaoEstoque.Ajuste);
        mov.Quantidade.Value.Should().Be(4);
        mov.ValorTotal!.Valor.Should().Be(40m);
        mov.VendaId.Should().BeNull();
        mov.ItemEstoqueId.Should().Be(item.Id);
        mov.ProdutoId.Should().Be(item.ProdutoId);
        mov.DocumentoReferencia.Should().Be($"contagem:{contagemId}");
    }

    [Fact]
    public void Delta_negativo_vira_saida_com_qtd_absoluta_sem_venda()
    {
        var item = CriarItem();

        var mov = MovimentacaoEstoque.CriarAjusteContagem(
            Guid.NewGuid(), item.EmpresaId, item, delta: -3m,
            Dinheiro.FromDecimal(10m), Quando, Guid.NewGuid(), null, Quando);

        mov.Tipo.Should().Be(TipoMovimentacaoEstoque.Saida);
        mov.Natureza.Should().Be(NaturezaMovimentacaoEstoque.Ajuste);
        mov.Quantidade.Value.Should().Be(3);
        mov.ValorTotal!.Valor.Should().Be(30m);
        mov.VendaId.Should().BeNull();
    }

    [Fact]
    public void Delta_zero_lanca()
    {
        var item = CriarItem();
        Action act = () => MovimentacaoEstoque.CriarAjusteContagem(
            Guid.NewGuid(), item.EmpresaId, item, delta: 0m,
            Dinheiro.FromDecimal(10m), Quando, Guid.NewGuid(), null, Quando);
        act.Should().Throw<RegraDeDominioVioladaException>();
    }

    private static ItemEstoque CriarItem() =>
        new()
        {
            Id = Guid.NewGuid(),
            EmpresaId = Guid.NewGuid(),
            ProdutoId = Guid.NewGuid(),
            QuantidadeInicial = Quantidade.From(10),
            QuantidadeAtual = Quantidade.From(10),
            CustoUnitario = Dinheiro.FromDecimal(100m),
            Status = StatusItemEstoque.Ok,
            EntradaEm = Quando,
            CriadoEm = Quando,
            AlteradoEm = Quando
        };
}
