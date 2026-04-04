using EasyStock.Api.Controllers;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Entities;
using EasyStock.Domain.ValueObjects;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Xunit;

namespace EasyStock.Api.UnitTests.Controllers;

public class InteligenciaControllerTests
{
    private readonly IItemEstoqueRepository _itemEstoqueRepository = Substitute.For<IItemEstoqueRepository>();
    private readonly InteligenciaController _controller;

    public InteligenciaControllerTests()
    {
        _controller = new InteligenciaController(_itemEstoqueRepository);
    }

    [Fact]
    public async Task EstoqueBaixo_DeveRetornarOk_ComItensAbaixoDoLimite()
    {
        // Arrange
        var item1 = new ItemEstoque { Id = Guid.NewGuid(), QuantidadeAtual = Quantidade.From(5) };
        var item2 = new ItemEstoque { Id = Guid.NewGuid(), QuantidadeAtual = Quantidade.From(15) };
        var itens = new List<ItemEstoque> { item1, item2 };
        _itemEstoqueRepository.GetAllAsync().Returns(itens);

        // Act
        var result = await _controller.EstoqueBaixo(10);

        // Assert
        result.Should().BeOfType<OkObjectResult>();
        var okResult = result as OkObjectResult;
        var returnedItens = okResult!.Value as IEnumerable<ItemEstoque>;
        returnedItens.Should().ContainSingle().Which.Should().Be(item1);
    }

    [Fact]
    public async Task ProximoVencimento_DeveRetornarOk_ComItensProximosAoVencimento()
    {
        // Arrange
        var hoje = DateTime.UtcNow.Date;
        var item1 = new ItemEstoque { Id = Guid.NewGuid(), ValidadeEm = Validade.From(hoje.AddDays(25)) };
        var item2 = new ItemEstoque { Id = Guid.NewGuid(), ValidadeEm = Validade.From(hoje.AddDays(35)) };
        var itens = new List<ItemEstoque> { item1, item2 };
        _itemEstoqueRepository.GetAllAsync().Returns(itens);

        // Act
        var result = await _controller.ProximoVencimento(30);

        // Assert
        result.Should().BeOfType<OkObjectResult>();
        var okResult = result as OkObjectResult;
        var returnedItens = okResult!.Value as IEnumerable<ItemEstoque>;
        returnedItens.Should().ContainSingle().Which.Should().Be(item1);
    }

    [Fact]
    public async Task ItensParados_DeveRetornarOk_ComItensSemMovimentacao()
    {
        // Arrange
        var hoje = DateTime.UtcNow;
        var item1 = new ItemEstoque { Id = Guid.NewGuid(), UltimaMovimentacaoEm = hoje.AddDays(-100) };
        var item2 = new ItemEstoque { Id = Guid.NewGuid(), UltimaMovimentacaoEm = hoje.AddDays(-50) };
        var itens = new List<ItemEstoque> { item1, item2 };
        _itemEstoqueRepository.GetAllAsync().Returns(itens);

        // Act
        var result = await _controller.ItensParados(90);

        // Assert
        result.Should().BeOfType<OkObjectResult>();
        var okResult = result as OkObjectResult;
        var returnedItens = okResult!.Value as IEnumerable<ItemEstoque>;
        returnedItens.Should().ContainSingle().Which.Should().Be(item1);
    }

    [Fact]
    public async Task SugestaoReposicao_DeveRetornarOk_ComItensComEstoqueBaixo()
    {
        // Arrange
        var item1 = new ItemEstoque { Id = Guid.NewGuid(), QuantidadeAtual = Quantidade.From(3) };
        var item2 = new ItemEstoque { Id = Guid.NewGuid(), QuantidadeAtual = Quantidade.From(10) };
        var itens = new List<ItemEstoque> { item1, item2 };
        _itemEstoqueRepository.GetAllAsync().Returns(itens);

        // Act
        var result = await _controller.SugestaoReposicao();

        // Assert
        result.Should().BeOfType<OkObjectResult>();
        var okResult = result as OkObjectResult;
        var returnedItens = okResult!.Value as IEnumerable<ItemEstoque>;
        returnedItens.Should().ContainSingle().Which.Should().Be(item1);
    }
}
