using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.GerenciarProduto;
using EasyStock.Application.UseCases.GerenciarProduto.Comandos;
using EasyStock.TestHelpers;

namespace EasyStock.Application.Tests.UseCases;

/// <summary>#2737 (EST-01): inativar pela edição passa pela mesma guarda do Remover.</summary>
public class AtualizarProdutoUseCaseTests
{
    private readonly IProdutoRepository _produtos = Substitute.For<IProdutoRepository>();
    private readonly ICategoriaRepository _categorias = Substitute.For<ICategoriaRepository>();
    private readonly IItemEstoqueRepository _itens = Substitute.For<IItemEstoqueRepository>();
    private readonly IPedidoRepository _pedidos = Substitute.For<IPedidoRepository>();
    private readonly Guid _empresaId = Guid.NewGuid();
    private readonly Guid _produtoId = Guid.NewGuid();
    private readonly Guid _categoriaId = Guid.NewGuid();

    private AtualizarProdutoUseCase Sut() => new(
        _produtos, _categorias, Substitute.For<IProdutoVariacaoRepository>(),
        Substitute.For<IProdutoCaracteristicaRepository>(), Substitute.For<IProdutoEmbalagemRepository>(),
        new FakeUnitOfWork(), itemEstoqueRepository: _itens, pedidoRepository: _pedidos);

    private Produto ProdutoAtivo()
    {
        var produto = new Produto
        {
            Id = _produtoId, EmpresaId = _empresaId, CategoriaId = _categoriaId,
            Nome = "Lasanha", Status = StatusProduto.Ativo,
        };
        _produtos.GetByIdParaAtualizarAsync(_empresaId, _produtoId).Returns(produto);
        _categorias.GetByIdAsync(_empresaId, _categoriaId).Returns(new Categoria
        {
            Id = _categoriaId, EmpresaId = _empresaId, Nome = "Categoria",
        });
        return produto;
    }

    private AtualizarProdutoCommand Comando(StatusProduto status, decimal? preco = 10m) => new(
        _empresaId, _produtoId, _categoriaId, null, "Lasanha",
        null, null, TipoProduto.Fisico, null, null, false, null,
        null, preco, null, null, status,
        null, null, null);

    [Fact]
    public async Task AtualizarProduto_StatusInativo_ComEstoque_LancaUseCaseValidationException()
    {
        var produto = ProdutoAtivo();
        _itens.ExisteEstoqueDoProdutoAsync(_empresaId, _produtoId).Returns(true);

        var act = () => Sut().ExecuteAsync(Comando(StatusProduto.Inativo));

        await act.Should().ThrowAsync<UseCaseValidationException>().WithMessage("*estoque disponivel*");
        produto.Status.Should().Be(StatusProduto.Ativo);
        await _produtos.DidNotReceive().UpdateAsync(Arg.Any<Produto>());
    }

    [Fact]
    public async Task AtualizarProduto_StatusInativo_ComPedidoAberto_LancaUseCaseValidationException()
    {
        var produto = ProdutoAtivo();
        _pedidos.ExistemPedidosAbertosComProdutoAsync(_empresaId, _produtoId).Returns(true);

        var act = () => Sut().ExecuteAsync(Comando(StatusProduto.Inativo));

        await act.Should().ThrowAsync<UseCaseValidationException>().WithMessage("*pedidos abertos*");
        produto.Status.Should().Be(StatusProduto.Ativo);
    }

    [Fact]
    public async Task AtualizarProduto_StatusInativo_SemEstoqueNemPedido_Inativa()
    {
        var produto = ProdutoAtivo();

        await Sut().ExecuteAsync(Comando(StatusProduto.Inativo));

        produto.Status.Should().Be(StatusProduto.Inativo);
    }

    [Fact]
    public async Task AtualizarProduto_GateDePreco_ComEstoque_InativaSemLancar()
    {
        // O gate automático (Ativo sem preço vira Inativo) não é escolha da pessoa e não passa pela guarda.
        var produto = ProdutoAtivo();
        _itens.ExisteEstoqueDoProdutoAsync(_empresaId, _produtoId).Returns(true);

        await Sut().ExecuteAsync(Comando(StatusProduto.Ativo, preco: null));

        produto.Status.Should().Be(StatusProduto.Inativo);
    }

    [Fact]
    public async Task AtualizarProduto_JaInativoComEstoque_ContinuaInativoSemLancar()
    {
        var produto = ProdutoAtivo();
        produto.Status = StatusProduto.Inativo;
        _itens.ExisteEstoqueDoProdutoAsync(_empresaId, _produtoId).Returns(true);

        await Sut().ExecuteAsync(Comando(StatusProduto.Inativo));

        produto.Status.Should().Be(StatusProduto.Inativo);
    }
}
