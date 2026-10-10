using EasyStock.Api.Authorization;
using EasyStock.Api.Controllers;
using EasyStock.Domain.Enums;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;

namespace EasyStock.Api.UnitTests.Authorization;

/// <summary>
/// #1508: POST /api/uploads/produto/{id}/foto trocava a foto do produto para qualquer autenticado, enquanto
/// o mesmo upload em /api/produtos/{id}/fotos exige Gerente no módulo Cardápio. As duas rotas usam o mesmo
/// caso de uso e passam a ter a mesma autorização.
/// </summary>
public class UploadFotoProdutoAutorizacaoTests
{
    [Fact]
    public void UploadFotoProduto_ExigeAMesmaPolicyDoUploadEmProdutos()
    {
        string? Policy(Type controller, string acao) =>
            controller.GetMethod(acao)!.GetCustomAttributes(typeof(AuthorizeAttribute), true)
                .Cast<AuthorizeAttribute>().Single().Policy;

        Policy(typeof(UploadsController), nameof(UploadsController.UploadFotoProduto))
            .Should().Be("Gerente")
            .And.Be(Policy(typeof(ProdutoController), nameof(ProdutoController.UploadFoto)));
    }

    [Fact]
    public void UploadFotoProduto_ExigeModuloCardapio_SemPrenderOsOutrosUploads()
    {
        ModulosConvention.ModulosDe(typeof(UploadsController), nameof(UploadsController.UploadFotoProduto))
            .Should().Equal(ModulosConvention.ModulosDe(typeof(ProdutoController), nameof(ProdutoController.UploadFoto)))
            .And.Equal(Modulo.Cardapio);
        ModulosConvention.ModulosDe(typeof(UploadsController), nameof(UploadsController.UploadAvatar)).Should().BeNull();
    }
}
