using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Domain.ValueObjects;
using FluentAssertions;

namespace EasyStock.Domain.Tests.Entities;

// #582 / ADR-0033: completude calculada no dominio (fonte unica de lista e detalhe).
public class ProdutoCompletudeTests
{
    [Fact]
    public void Produto_minimo_pontua_so_variacoes_nome_categoria()
    {
        var p = new Produto { Nome = "X", CategoriaId = Guid.NewGuid(), Tipo = TipoProduto.Fisico };

        p.CompletudePercent.Should().Be(15); // 10 variacoes + 3 nome + 2 categoria (sempre)
        p.Pendencias.Should().Contain(new[] { "Foto", "Descrição", "Custo", "Preço", "Cód.Barras", "Marca", "Dimensões" });
        p.Pendencias.Should().NotContain("Nutricional"); // nao e alimento, sem ficha
    }

    [Fact]
    public void Produto_quase_completo_sem_dimensoes_da_95_e_so_falta_dimensoes()
    {
        var p = new Produto
        {
            Nome = "X",
            CategoriaId = Guid.NewGuid(),
            Tipo = TipoProduto.Fisico,
            FotosJson = "[{\"url\":\"a.jpg\"}]",
            DescricaoBase = "desc",
            CustoReferencia = Dinheiro.FromDecimal(5m),
            PrecoReferencia = Dinheiro.FromDecimal(10m),
            CodigoBarras = "7891234567890",
            Marca = "Acme",
        };

        p.CompletudePercent.Should().Be(95); // 15 base + 20+15+15+15+10+5
        p.Pendencias.Should().BeEquivalentTo(new[] { "Dimensões" });
    }

    [Fact]
    public void Alimento_sem_ficha_lista_nutricional_como_pendencia()
    {
        var p = new Produto { Nome = "X", CategoriaId = Guid.NewGuid(), Tipo = TipoProduto.Alimento };

        p.Pendencias.Should().Contain("Nutricional");
    }

    [Fact]
    public void Ficha_tecnica_preenchida_soma_dez_e_some_da_pendencia()
    {
        var semFicha = new Produto { Nome = "X", CategoriaId = Guid.NewGuid(), Tipo = TipoProduto.Alimento };
        var comFicha = new Produto { Nome = "X", CategoriaId = Guid.NewGuid(), Tipo = TipoProduto.Alimento, AtributosJson = "{\"kcal\":100}" };

        (comFicha.CompletudePercent - semFicha.CompletudePercent).Should().Be(10);
        comFicha.Pendencias.Should().NotContain("Nutricional");
    }

    [Fact]
    public void FotosJson_vazio_ou_array_vazio_nao_conta_como_foto()
    {
        var vazio = new Produto { Nome = "X", CategoriaId = Guid.NewGuid(), Tipo = TipoProduto.Fisico, FotosJson = "[]" };
        var nulo = new Produto { Nome = "X", CategoriaId = Guid.NewGuid(), Tipo = TipoProduto.Fisico, FotosJson = null };

        vazio.Pendencias.Should().Contain("Foto");
        nulo.Pendencias.Should().Contain("Foto");
    }

    [Fact]
    public void Produto_totalmente_preenchido_clampa_em_100_nao_110()
    {
        // Os pesos somam 110 no máximo (15 base + 95 condicional); um produto 100% preenchido
        // não pode exibir "110% completo". Regressão do clamp (Math.Min(pct, 100)).
        var p = new Produto
        {
            Nome = "X",
            CategoriaId = Guid.NewGuid(),
            Tipo = TipoProduto.Alimento,
            FotosJson = "[{\"url\":\"a.jpg\"}]",
            DescricaoBase = "desc",
            CustoReferencia = Dinheiro.FromDecimal(5m),
            PrecoReferencia = Dinheiro.FromDecimal(10m),
            CodigoBarras = "7891234567890",
            Marca = "Acme",
            Dimensoes = Dimensoes.From(0.5m, 35m, 25m, 45m),
            AtributosJson = "{\"kcal\":100}",
        };

        p.CompletudePercent.Should().Be(100);
        p.Pendencias.Should().BeEmpty();
    }
}
