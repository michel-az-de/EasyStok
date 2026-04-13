using EasyStock.Api.Controllers;
using EasyStock.Api.Http;
using EasyStock.Application.Ports.Output;
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
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();
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
            _produtoCaracteristicaRepository,
            _produtoEmbalagemRepository,
            _itemEstoqueRepository,
            _movimentacaoEstoqueRepository,
            _unitOfWork);

        var gerenciarVariacaoProdutoUseCase = new GerenciarVariacaoProdutoUseCase(
            _produtoRepository,
            _produtoVariacaoRepository,
            _itemEstoqueRepository,
            _unitOfWork,
            Substitute.For<ILogger<GerenciarVariacaoProdutoUseCase>>());

        var gerenciarUploadsUseCase = new GerenciarUploadsUseCase(
            _fileStorage,
            Substitute.For<IImageProcessor>(),
            _produtoRepository,
            _usuarioRepository,
            _lojaRepository,
            _unitOfWork);

        _controller = new ProdutoController(_produtoRepository, _cadastrarProdutoUseCase, gerenciarProdutoUseCase, gerenciarVariacaoProdutoUseCase, gerenciarUploadsUseCase, _currentUser);
    }

    [Fact]
    public async Task GetAll_DeveRetornarOk_ComListaDeProdutos()
    {
        var empresaId = Guid.NewGuid();
        var produtos = new List<Produto> { new Produto { Id = Guid.NewGuid(), Nome = "Produto1" } };
        _produtoRepository.GetProdutosPaginadosAsync(empresaId, 1, 20).Returns((produtos, 1));

        var result = await _controller.GetAll(empresaId);

        result.Should().BeOfType<OkObjectResult>();
        var envelope = ((OkObjectResult)result).Value.Should().BeOfType<ApiResponse<IEnumerable<Produto>>>().Subject;
        envelope.Data.Should().BeEquivalentTo(produtos);
        envelope.Meta.Should().BeOfType<PagedMeta>().Which.Total.Should().Be(1);
    }

    [Fact]
    public async Task GetById_DeveRetornarOk_QuandoProdutoEncontrado()
    {
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
        _produtoCaracteristicaRepository.GetByProdutoAsync(empresaId, produto.Id).Returns(Enumerable.Empty<ProdutoCaracteristica>());
        _produtoEmbalagemRepository.GetByProdutoAsync(empresaId, produto.Id).Returns(Enumerable.Empty<ProdutoEmbalagem>());

        var result = await _controller.GetById(produto.Id, empresaId);

        result.Should().BeOfType<OkObjectResult>();
        var envelope = ((OkObjectResult)result).Value.Should().BeOfType<ApiResponse<ProdutoDetalheResult>>().Subject;
        envelope.Data.ProdutoId.Should().Be(produto.Id);
    }

    [Fact]
    public async Task GetById_DeveRetornarNotFound_QuandoProdutoNaoEncontrado()
    {
        var empresaId = Guid.NewGuid();
        var id = Guid.NewGuid();
        _produtoRepository.GetDetalheAsync(empresaId, id).Returns((Produto?)null);

        var result = await _controller.GetById(id, empresaId);

        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task Search_DeveRetornarOk_ComProdutosFiltrados()
    {
        var empresaId = Guid.NewGuid();
        var termo = "teste";
        var produtos = new List<Produto> { new Produto { Id = Guid.NewGuid(), Nome = "Produto Teste" } };
        _produtoRepository.SearchAsync(empresaId, termo).Returns(produtos);

        var result = await _controller.Search(empresaId, termo);

        result.Should().BeOfType<OkObjectResult>();
        var envelope = ((OkObjectResult)result).Value.Should().BeOfType<ApiResponse<IEnumerable<Produto>>>().Subject;
        envelope.Data.Should().BeEquivalentTo(produtos);
    }

    [Fact]
    public async Task Create_DeveRetornarCreated_ComResultado()
    {
        var empresaId = Guid.NewGuid();
        var categoriaId = Guid.NewGuid();
        var command = new CadastrarProdutoCommand(
            empresaId,
            categoriaId,
            null,
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

        result.Should().BeOfType<CreatedResult>();
        var createdResult = (CreatedResult)result;
        var envelope = createdResult.Value.Should().BeOfType<ApiResponse<CadastrarProdutoResult>>().Subject;
        envelope.Data.ProdutoId.Should().NotBe(Guid.Empty);
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

}
