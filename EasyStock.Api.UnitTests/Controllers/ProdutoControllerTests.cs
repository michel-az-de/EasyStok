using EasyStock.Api.Controllers;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.CadastrarProduto;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace EasyStock.Api.UnitTests.Controllers;

public class ProdutoControllerTests
{
    private readonly IProdutoRepository _produtoRepository = Substitute.For<IProdutoRepository>();
    private readonly ICategoriaRepository _categoriaRepository = Substitute.For<ICategoriaRepository>();
    private readonly IProdutoCaracteristicaRepository _produtoCaracteristicaRepository = Substitute.For<IProdutoCaracteristicaRepository>();
    private readonly IProdutoEmbalagemRepository _produtoEmbalagemRepository = Substitute.For<IProdutoEmbalagemRepository>();
    private readonly IProdutoVariacaoRepository _produtoVariacaoRepository = Substitute.For<IProdutoVariacaoRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly ILogger<CadastrarProdutoUseCase> _logger = Substitute.For<ILogger<CadastrarProdutoUseCase>>();
    private readonly CadastrarProdutoUseCase _cadastrarProdutoUseCase;
    private readonly ProdutoController _controller;

    public ProdutoControllerTests()
    {
        _cadastrarProdutoUseCase = new CadastrarProdutoUseCase(
            _produtoRepository,
            _categoriaRepository,
            _produtoCaracteristicaRepository,
            _produtoEmbalagemRepository,
            _produtoVariacaoRepository,
            _unitOfWork,
            _logger);
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
        okResult!.Value.Should().BeEquivalentTo(produtos);
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
        okResult!.Value.Should().Be(produto);
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
        okResult!.Value.Should().BeEquivalentTo(produtos);
    }

    [Fact]
    public async Task Create_DeveRetornarCreated_ComResultado()
    {
        var empresaId = Guid.NewGuid();
        var categoriaId = Guid.NewGuid();
        var command = new CadastrarProdutoCommand(
            empresaId,
            categoriaId,
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
        _categoriaRepository.GetByIdAsync(categoriaId).Returns(new Categoria
        {
            Id = categoriaId,
            EmpresaId = empresaId,
            Nome = "Categoria"
        });
        _unitOfWork.CommitAsync().Returns(1);

        var result = await _controller.Create(command);

        result.Should().BeOfType<CreatedAtActionResult>();
        var createdResult = result as CreatedAtActionResult;
        createdResult!.Value.Should().BeOfType<CadastrarProdutoResult>();
        var payload = createdResult.Value as CadastrarProdutoResult;
        payload!.ProdutoId.Should().NotBe(Guid.Empty);
        createdResult.ActionName.Should().Be("GetById");
        await _produtoRepository.Received(1).AddAsync(Arg.Any<Produto>());
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
