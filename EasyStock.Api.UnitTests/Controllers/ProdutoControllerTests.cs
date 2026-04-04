using EasyStock.Api.Controllers;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.CadastrarProduto;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Xunit;

namespace EasyStock.Api.UnitTests.Controllers;

public class ProdutoControllerTests
{
    private readonly IProdutoRepository _produtoRepository = Substitute.For<IProdutoRepository>();
    private readonly CadastrarProdutoUseCase _cadastrarProdutoUseCase = Substitute.For<CadastrarProdutoUseCase>();
    private readonly ProdutoController _controller;

    public ProdutoControllerTests()
    {
        _controller = new ProdutoController(_produtoRepository, _cadastrarProdutoUseCase);
    }

    [Fact]
    public async Task GetAll_DeveRetornarOk_ComListaDeProdutos()
    {
        // Arrange
        var produtos = new List<Produto> { new Produto { Id = Guid.NewGuid(), Nome = "Produto1" } };
        _produtoRepository.GetAllAsync().Returns(produtos);

        // Act
        var result = await _controller.GetAll();

        // Assert
        result.Should().BeOfType<OkObjectResult>();
        var okResult = result as OkObjectResult;
        okResult.Value.Should().BeEquivalentTo(produtos);
    }

    [Fact]
    public async Task GetById_DeveRetornarOk_QuandoProdutoEncontrado()
    {
        // Arrange
        var produto = new Produto { Id = Guid.NewGuid(), Nome = "Produto1" };
        _produtoRepository.GetByIdAsync(produto.Id).Returns(produto);

        // Act
        var result = await _controller.GetById(produto.Id);

        // Assert
        result.Should().BeOfType<OkObjectResult>();
        var okResult = result as OkObjectResult;
        okResult.Value.Should().Be(produto);
    }

    [Fact]
    public async Task GetById_DeveRetornarNotFound_QuandoProdutoNaoEncontrado()
    {
        // Arrange
        var id = Guid.NewGuid();
        _produtoRepository.GetByIdAsync(id).Returns((Produto?)null);

        // Act
        var result = await _controller.GetById(id);

        // Assert
        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task Search_DeveRetornarOk_ComProdutosFiltrados()
    {
        // Arrange
        var empresaId = Guid.NewGuid();
        var termo = "teste";
        var produtos = new List<Produto> { new Produto { Id = Guid.NewGuid(), Nome = "Produto Teste" } };
        _produtoRepository.SearchAsync(empresaId, termo).Returns(produtos);

        // Act
        var result = await _controller.Search(empresaId, termo);

        // Assert
        result.Should().BeOfType<OkObjectResult>();
        var okResult = result as OkObjectResult;
        okResult.Value.Should().BeEquivalentTo(produtos);
    }

    [Fact]
    public async Task Create_DeveRetornarCreated_ComResultado()
    {
        // Arrange
        var command = new CadastrarProdutoCommand(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Produto",
            null,
            null,
            TipoProduto.Fisico,
            null,
            null,
            false,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null);
        var resultado = new CadastrarProdutoResult(Guid.NewGuid(), new List<Guid>(), new List<Guid>(), new List<Guid>());
        _cadastrarProdutoUseCase.ExecuteAsync(command).Returns(resultado);

        // Act
        var result = await _controller.Create(command);

        // Assert
        result.Should().BeOfType<CreatedAtActionResult>();
        var createdResult = result as CreatedAtActionResult;
        createdResult.Value.Should().Be(resultado);
        createdResult.ActionName.Should().Be("GetById");
    }

    [Fact]
    public async Task Update_DeveRetornarNoContent_QuandoSucesso()
    {
        // Arrange
        var produto = new Produto { Id = Guid.NewGuid(), Nome = "Produto Atualizado" };

        // Act
        var result = await _controller.Update(produto.Id, produto);

        // Assert
        result.Should().BeOfType<NoContentResult>();
        await _produtoRepository.Received(1).UpdateAsync(produto);
    }

    [Fact]
    public async Task Update_DeveRetornarBadRequest_QuandoIdsNaoCoincidem()
    {
        // Arrange
        var produto = new Produto { Id = Guid.NewGuid(), Nome = "Produto" };

        // Act
        var result = await _controller.Update(Guid.NewGuid(), produto);

        // Assert
        result.Should().BeOfType<BadRequestResult>();
    }

    [Fact]
    public async Task Delete_DeveRetornarNoContent()
    {
        // Arrange
        var id = Guid.NewGuid();

        // Act
        var result = await _controller.Delete(id);

        // Assert
        result.Should().BeOfType<NoContentResult>();
        await _produtoRepository.Received(1).DeleteAsync(id);
    }
}
