using EasyStock.Web.Models.Api;
using EasyStock.Web.Models.ViewModels.Produtos;
using FluentAssertions;

namespace EasyStock.Web.UnitTests.ViewModels;

/// <summary>
/// #582 / ADR-0033: a completude passou a vir PRONTA do backend (fonte unica de lista e
/// detalhe). O ProdutoDetailViewModel apenas REPASSA Produto.CompletudePercent -> IntegrityScore
/// e Produto.Pendencias -> IntegrityMissing. O calculo ponderado (incl. "Variacoes opcional",
/// BUG-61/62 #450) vive e e testado no dominio:
/// EasyStock.Domain.Tests/Entities/ProdutoCompletudeTests. Aqui so verificamos o repasse.
/// </summary>
public class ProdutoDetailViewModelTests
{
    [Fact]
    public void IntegrityScore_repassa_CompletudePercent_do_backend()
    {
        var vm = new ProdutoDetailViewModel
        {
            Produto = new ProdutoDetalhe { Nome = "Produto Teste", CompletudePercent = 73 },
        };

        vm.IntegrityScore.Should().Be(73);
    }

    [Fact]
    public void IntegrityMissing_repassa_Pendencias_do_backend()
    {
        var vm = new ProdutoDetailViewModel
        {
            Produto = new ProdutoDetalhe { Nome = "Produto Teste", Pendencias = ["Foto", "Preço"] },
        };

        vm.IntegrityMissing.Should().Equal("Foto", "Preço");
    }

    [Fact]
    public void IntegrityMissing_vazio_quando_backend_nao_reporta_pendencias()
    {
        var vm = new ProdutoDetailViewModel
        {
            Produto = new ProdutoDetalhe { Nome = "Produto Teste" },
        };

        vm.IntegrityMissing.Should().BeEmpty();
    }
}
