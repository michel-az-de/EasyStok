using EasyStock.Web.Models.Api;
using EasyStock.Web.Models.ViewModels.Produtos;
using FluentAssertions;

namespace EasyStock.Web.UnitTests.ViewModels;

/// <summary>
/// BUG-61/62 (#450): "Variações" é opcional na completude. Produto sem variação
/// (simples) não deve ser penalizado nem nunca alcançar 100%. O score é soma crua
/// (campos base somam 100), então os 10 pts de Variações são sempre contados.
/// </summary>
public class ProdutoDetailViewModelTests
{
    private const string LabelVariacoes = "Variações";

    private static ProdutoDetalhe ProdutoBase(bool comVariacao) => new()
    {
        Nome = "Produto Teste",
        Tipo = 0, // não-alimento => Nutricional não entra na completude
        Variacoes = comVariacao
            ? [new VariacaoDetalhe { VariacaoId = Guid.NewGuid(), Nome = "Padrão" }]
            : [],
    };

    private static ProdutoDetalhe ProdutoCompletoSimples() => ProdutoBase(comVariacao: false) with
    {
        DescricaoBase = "descrição",
        Marca = "Marca",
        SkuBase = "SKU1",
        CodigoBarras = "7891234567895",
        CustoReferencia = 10m,
        PrecoReferencia = 12m,
        Dimensoes = new ProdutoDimensoesDetalhe(1m, 1m, 1m, 1m),
        Fotos = [new ProdutoFotoDetalhe(Guid.NewGuid(), "/img/x.jpg", DateTime.UtcNow)],
    };

    [Fact]
    public void ProdutoSimplesCompleto_Atinge100_SemVariacoesEmFalta()
    {
        var vm = new ProdutoDetailViewModel { Produto = ProdutoCompletoSimples() };

        vm.IntegrityScore.Should().Be(100);
        vm.IntegrityMissing.Should().NotContain(LabelVariacoes);
        vm.IntegrityMissing.Should().BeEmpty();
    }

    [Fact]
    public void ProdutoSimplesVazio_NaoListaVariacoesComoFaltante()
    {
        var vm = new ProdutoDetailViewModel { Produto = ProdutoBase(comVariacao: false) };

        // Nome (3) + Categoria (2) + Variações (10, agora opcional) = 15.
        vm.IntegrityScore.Should().Be(15);
        vm.IntegrityMissing.Should().NotContain(LabelVariacoes);
        vm.IntegrityMissing.Should().Contain("Foto");
    }

    [Fact]
    public void ProdutoComVariacao_NaoListaVariacoesComoFaltante()
    {
        var vm = new ProdutoDetailViewModel { Produto = ProdutoBase(comVariacao: true) };

        vm.IntegrityMissing.Should().NotContain(LabelVariacoes);
    }
}
