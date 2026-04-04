using EasyStock.Api.Controllers;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Storage;
using EasyStock.Application.UseCases.CadastrarProduto;
using EasyStock.Application.UseCases.Common;
using EasyStock.Application.UseCases.GerenciarProduto;
using EasyStock.Application.UseCases.GerenciarUploads;
using EasyStock.Application.UseCases.GerenciarVariacaoProduto;
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
    private readonly IItemEstoqueRepository _itemEstoqueRepository = Substitute.For<IItemEstoqueRepository>();
    private readonly IMovimentacaoEstoqueRepository _movimentacaoEstoqueRepository = Substitute.For<IMovimentacaoEstoqueRepository>();
    private readonly ILojaRepository _lojaRepository = Substitute.For<ILojaRepository>();
    private readonly IUsuarioRepository _usuarioRepository = Substitute.For<IUsuarioRepository>();
    private readonly IFileStorage _fileStorage = Substitute.For<IFileStorage>();
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

        var gerenciarProdutoUseCase = new GerenciarProdutoUseCase(
            _produtoRepository,
            _categoriaRepository,
            _produtoVariacaoRepository,
            _itemEstoqueRepository,
            _movimentacaoEstoqueRepository,
            _unitOfWork);

        var gerenciarVariacaoProdutoUseCase = new GerenciarVariacaoProdutoUseCase(
            _produtoRepository,
            _produtoVariacaoRepository,
            _itemEstoqueRepository,
            _unitOfWork);

        var gerenciarUploadsUseCase = new GerenciarUploadsUseCase(
            _fileStorage,
            _produtoRepository,
            _usuarioRepository,
            _lojaRepository,
            _unitOfWork);

        _controller = new ProdutoController(_produtoRepository, _cadastrarProdutoUseCase, gerenciarProdutoUseCase, gerenciarVariacaoProdutoUseCase, gerenciarUploadsUseCase);
    }

    [Fact]
    public async Task GetAll_DeveRetornarOk_ComListaDeProdutos()
    {
        // Arrange
        var empresaId = Guid.NewGuid();
        var produtos = new List<Produto> { new Produto { Id = Guid.NewGuid(), Nome = "Produto1" } };
        _produtoRepository.GetProdutosPaginadosAsync(empresaId, 1, 20).Returns((produtos, 1));

        // Act
        var result = await _controller.GetAll(empresaId);

        // Assert
        result.Should().BeOfType<OkObjectResult>();
        var okResult = result as OkObjectResult;
        var produtosRetornados = ObterPropriedade<IEnumerable<Produto>>(okResult!.Value, "Produtos");
        var totalCount = ObterPropriedade<int>(okResult.Value, "TotalCount");
        produtosRetornados.Should().BeEquivalentTo(produtos);
        totalCount.Should().Be(1);
    }
    [Fact]
    public async Task GetById_DeveRetornarOk_QuandoProdutoEncontrado()
    {
        // Arrange
        var empresaId = Guid.NewGuid();
        var produto = new Produto
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            CategoriaId = Guid.NewGuid(),
            Nome = "Produto1",
            Status = StatusProduto.Ativo
        };
        _produtoRepository.GetDetalheAsync(empresaId, produto.Id).Returns(produto);
        _produtoVariacaoRepository.GetByProdutoAsync(empresaId, produto.Id).Returns([]);
        _itemEstoqueRepository.GetByProdutoAsync(empresaId, produto.Id).Returns([]);

        // Act
        var result = await _controller.GetById(produto.Id, empresaId);

        // Assert
        result.Should().BeOfType<OkObjectResult>();
        var okResult = result as OkObjectResult;
        okResult!.Value.Should().BeOfType<ProdutoDetalheResult>();
        var payload = okResult.Value as ProdutoDetalheResult;
        payload!.ProdutoId.Should().Be(produto.Id);
    }

    [Fact]
    public async Task GetById_DeveRetornarNotFound_QuandoProdutoNaoEncontrado()
    {
        // Arrange
        var empresaId = Guid.NewGuid();
        var id = Guid.NewGuid();
        _produtoRepository.GetDetalheAsync(empresaId, id).Returns((Produto?)null);

        // Act
        var result = await _controller.GetById(id, empresaId);

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
        await _produtoRepository.Received(1).InsertAsync(Arg.Any<Produto>());
    }

    [Fact]
    public async Task Delete_DeveRetornarNoContent_QuandoProdutoPodeSerInativado()
    {
        var empresaId = Guid.NewGuid();
        var produto = new Produto { Id = Guid.NewGuid(), EmpresaId = empresaId, Nome = "Produto", Status = StatusProduto.Ativo };
        _produtoRepository.GetByIdAsync(empresaId, produto.Id).Returns(produto);
        _itemEstoqueRepository.ExisteEstoqueDoProdutoAsync(empresaId, produto.Id).Returns(false);

        var result = await _controller.Delete(produto.Id, empresaId);

        result.Should().BeOfType<NoContentResult>();
        await _produtoRepository.Received(1).UpdateAsync(Arg.Is<Produto>(p => p.Status == StatusProduto.Inativo));
    }

    [Fact]
    public async Task Delete_DeveLancarValidation_QuandoProdutoPossuiEstoque()
    {
        var empresaId = Guid.NewGuid();
        var produto = new Produto { Id = Guid.NewGuid(), EmpresaId = empresaId, Nome = "Produto", Status = StatusProduto.Ativo };
        _produtoRepository.GetByIdAsync(empresaId, produto.Id).Returns(produto);
        _itemEstoqueRepository.ExisteEstoqueDoProdutoAsync(empresaId, produto.Id).Returns(true);

        var act = () => _controller.Delete(produto.Id, empresaId);

        await act.Should().ThrowAsync<UseCaseValidationException>()
            .WithMessage("*estoque disponivel*");
    }

    private static T ObterPropriedade<T>(object? source, string nome)
    {
        source.Should().NotBeNull();
        var propriedade = source!.GetType().GetProperty(nome);
        propriedade.Should().NotBeNull();
        return (T)propriedade!.GetValue(source)!;
    }
}
