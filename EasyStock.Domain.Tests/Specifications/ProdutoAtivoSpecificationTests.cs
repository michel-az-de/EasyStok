using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Domain.Specifications;
using FluentAssertions;

namespace EasyStock.Domain.Tests.Specifications;

public class ProdutoAtivoSpecificationTests
{
    [Fact]
    public void Deve_satisfazer_quando_produto_ativo()
    {
        var produto = new Produto { Status = StatusProduto.Ativo };
        var spec = new ProdutoAtivoSpecification();

        var satisfaz = spec.EhSatisfeitaPor(produto);

        satisfaz.Should().BeTrue();
    }

    [Fact]
    public void Nao_deve_satisfazer_quando_produto_inativo()
    {
        var produto = new Produto { Status = StatusProduto.Inativo };
        var spec = new ProdutoAtivoSpecification();

        var satisfaz = spec.EhSatisfeitaPor(produto);

        satisfaz.Should().BeFalse();
    }
}