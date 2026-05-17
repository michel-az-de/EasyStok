using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Domain.Exceptions;
using EasyStock.Domain.ValueObjects;
using FluentAssertions;

namespace EasyStock.Domain.Tests.Entities;

public class ItemEstoqueTests
{
    [Fact]
    public void Deve_criar_item_para_entrada_com_chave_pesquisa_e_status_ativo()
    {
        var produto = new Produto
        {
            Id = Guid.NewGuid(),
            Nome = "Galaxy Buds FE",
            SkuBase = CodigoSku.From("BUDS-FE"),
            PrecoReferencia = Dinheiro.FromDecimal(399.90m)
        };

        var variacao = new ProdutoVariacao
        {
            Id = Guid.NewGuid(),
            Nome = "Grafite",
            Cor = "Grafite",
            Tamanho = "Unico",
            Sku = CodigoSku.From("CAP3426")
        };

        var item = ItemEstoque.CriarParaEntrada(
            Guid.NewGuid(),
            Guid.NewGuid(),
            produto,
            variacao,
            Quantidade.From(10),
            Dinheiro.FromDecimal(250m),
            null,
            new DateTime(2026, 4, 3, 0, 0, 0, DateTimeKind.Utc),
            "CAP3426",
            null,
            "ML-1",
            null,
            null,
            null,
            "Descricao",
            null,
            "Fornecedor",
            null,
            null,
            DateTime.UtcNow);

        item.Status.Should().Be(StatusItemEstoque.Ok);
        item.QuantidadeInicial.Value.Should().Be(10);
        item.ChavePesquisa.Should().Contain("CAP3426");
        item.ChavePesquisa.Should().Contain("Galaxy Buds FE");
    }

    [Fact]
    public void Deve_repor_item_sem_desbloquear_status_bloqueado()
    {
        var item = CriarItem(status: StatusItemEstoque.Bloqueado);

        item.RegistrarReposicao(
            Quantidade.From(5),
            new DateTime(2026, 4, 3, 0, 0, 0, DateTimeKind.Utc),
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            DateTime.UtcNow);

        item.Status.Should().Be(StatusItemEstoque.Bloqueado);
        item.QuantidadeAtual.Value.Should().Be(15);
    }

    [Fact]
    public void Deve_registrar_saida_e_marcar_esgotado_quando_zerar()
    {
        var item = CriarItem(status: StatusItemEstoque.Ok, quantidadeAtual: 3);

        item.RegistrarSaida(Quantidade.From(3), new DateTime(2026, 4, 3, 0, 0, 0, DateTimeKind.Utc), DateTime.UtcNow);

        item.QuantidadeAtual.Value.Should().Be(0);
        item.Status.Should().Be(StatusItemEstoque.Esgotado);
    }

    [Fact]
    public void Nao_deve_permitir_saida_de_item_bloqueado()
    {
        var item = CriarItem(status: StatusItemEstoque.Bloqueado);

        Action act = () => item.RegistrarSaida(Quantidade.From(1), new DateTime(2026, 4, 3, 0, 0, 0, DateTimeKind.Utc), DateTime.UtcNow);

        act.Should().Throw<ItemEstoqueBloqueadoException>();
    }

    [Fact]
    public void Deve_marcar_vencido_ao_recalcular_status()
    {
        var item = CriarItem(status: StatusItemEstoque.Ok, validade: Validade.From(new DateTime(2026, 4, 1)));

        item.RecalcularStatus(new DateTime(2026, 4, 3, 0, 0, 0, DateTimeKind.Utc));

        item.Status.Should().Be(StatusItemEstoque.Vencido);
    }

    private static ItemEstoque CriarItem(StatusItemEstoque status, int quantidadeAtual = 10, Validade? validade = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            EmpresaId = Guid.NewGuid(),
            ProdutoId = Guid.NewGuid(),
            QuantidadeInicial = Quantidade.From(quantidadeAtual),
            QuantidadeAtual = Quantidade.From(quantidadeAtual),
            CustoUnitario = Dinheiro.FromDecimal(100m),
            Status = status,
            EntradaEm = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc),
            ValidadeEm = validade,
            CriadoEm = DateTime.UtcNow,
            AlteradoEm = DateTime.UtcNow
        };
}
