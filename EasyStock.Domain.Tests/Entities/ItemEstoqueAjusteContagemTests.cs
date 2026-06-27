using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Domain.ValueObjects;
using FluentAssertions;

namespace EasyStock.Domain.Tests.Entities;

public class ItemEstoqueAjusteContagemTests
{
    private static readonly DateTime Quando = new(2026, 6, 27, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Sobra_soma_delta_positivo_ao_atual()
    {
        // contado 7, esperado 5 -> delta +2; atual 5 -> 7
        var item = CriarItem(quantidadeAtual: 5);
        item.AplicarAjusteContagem(Quantidade.From(7), Quantidade.From(5), Quando, Quando);
        item.QuantidadeAtual.Value.Should().Be(7);
    }

    [Fact]
    public void Falta_aplica_delta_negativo_relativo_ao_atual_preservando_venda()
    {
        // Timeline do ADR: esperado 5, contado 3 (delta -2). Entre contar e aplicar, uma
        // venda de 2 deixou o ATUAL em 3. Aplicar relativo: 3 + (-2) = 1 (a venda sobrevive).
        var item = CriarItem(quantidadeAtual: 3);
        item.AplicarAjusteContagem(Quantidade.From(3), Quantidade.From(5), Quando, Quando);
        item.QuantidadeAtual.Value.Should().Be(1);
    }

    [Fact]
    public void Delta_zero_nao_altera_o_atual()
    {
        var item = CriarItem(quantidadeAtual: 8);
        item.AplicarAjusteContagem(Quantidade.From(8), Quantidade.From(8), Quando, Quando);
        item.QuantidadeAtual.Value.Should().Be(8);
    }

    [Fact]
    public void Resultado_negativo_pisa_em_zero_e_registra_descoberto()
    {
        // atual 1, esperado 4, contado 1 -> delta -3 -> alvo -2 -> pisa em 0 + descoberto 2
        var item = CriarItem(quantidadeAtual: 1);
        item.AplicarAjusteContagem(Quantidade.From(1), Quantidade.From(4), Quando, Quando);
        item.QuantidadeAtual.Value.Should().Be(0);
        item.QuantidadeDescoberta.Value.Should().Be(2);
        item.Status.Should().Be(StatusItemEstoque.Esgotado);
    }

    [Fact]
    public void Zera_descoberto_previo_quando_a_contagem_reconcilia()
    {
        var item = CriarItem(quantidadeAtual: 10);
        // simula oversell previo: atual 0, descoberto 5
        item.RegistrarSaidaPermitindoDescoberto(Quantidade.From(15), Quando, Quando);
        item.QuantidadeDescoberta.Value.Should().Be(5);

        // a contagem fisica acha 4; esperado 0 (atual). delta +4 -> atual 4, descoberto zerado.
        item.AplicarAjusteContagem(Quantidade.From(4), Quantidade.From(0), Quando, Quando);
        item.QuantidadeAtual.Value.Should().Be(4);
        item.QuantidadeDescoberta.Value.Should().Be(0);
    }

    [Fact]
    public void Lote_zerado_marca_esgotado()
    {
        // esperado 6, contado 0 -> delta -6; atual 6 -> 0
        var item = CriarItem(quantidadeAtual: 6);
        item.AplicarAjusteContagem(Quantidade.From(0), Quantidade.From(6), Quando, Quando);
        item.QuantidadeAtual.Value.Should().Be(0);
        item.Status.Should().Be(StatusItemEstoque.Esgotado);
    }

    [Fact]
    public void Lote_vencido_permanece_vencido_apos_ajuste()
    {
        var item = CriarItem(quantidadeAtual: 5, validade: Validade.From(new DateTime(2026, 6, 1)));
        item.AplicarAjusteContagem(Quantidade.From(3), Quantidade.From(5), Quando, Quando);
        item.QuantidadeAtual.Value.Should().Be(3);
        item.Status.Should().Be(StatusItemEstoque.Vencido);
    }

    [Fact]
    public void Aceita_quantidade_fracionaria()
    {
        var item = CriarItem(quantidadeAtual: 2.5m);
        item.AplicarAjusteContagem(Quantidade.From(1.25m), Quantidade.From(2.5m), Quando, Quando);
        item.QuantidadeAtual.Value.Should().Be(1.25m);
    }

    private static ItemEstoque CriarItem(decimal quantidadeAtual = 10m, Validade? validade = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            EmpresaId = Guid.NewGuid(),
            ProdutoId = Guid.NewGuid(),
            QuantidadeInicial = Quantidade.From(quantidadeAtual),
            QuantidadeAtual = Quantidade.From(quantidadeAtual),
            CustoUnitario = Dinheiro.FromDecimal(100m),
            Status = StatusItemEstoque.Ok,
            EntradaEm = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
            ValidadeEm = validade,
            CriadoEm = DateTime.UtcNow,
            AlteradoEm = DateTime.UtcNow
        };
}
