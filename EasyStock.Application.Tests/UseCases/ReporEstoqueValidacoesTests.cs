using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.ReporEstoque;
using EasyStock.Domain.ValueObjects;

namespace EasyStock.Application.Tests.UseCases;

/// <summary>
/// Cobre validacoes defensivas e propagacao de DocumentoReferencia em
/// ReporEstoque, alem dos casos felizes ja exercitados.
/// </summary>
public class ReporEstoqueValidacoesTests
{
    private static ReporEstoqueUseCase BuildUseCase(
        out IProdutoRepository produtoRepo,
        out IItemEstoqueRepository itemRepo,
        out IMovimentacaoEstoqueRepository movRepo,
        out IUnitOfWork uow)
    {
        produtoRepo = Substitute.For<IProdutoRepository>();
        itemRepo = Substitute.For<IItemEstoqueRepository>();
        movRepo = Substitute.For<IMovimentacaoEstoqueRepository>();
        uow = Substitute.For<IUnitOfWork>();
        return new ReporEstoqueUseCase(produtoRepo, itemRepo, movRepo, uow);
    }

    private static ReporEstoqueCommand CommandPadrao(
        Guid empresaId,
        Guid itemEstoqueId,
        int quantidadeAdicional = 5,
        decimal? novoCustoUnitario = null,
        decimal? novoPrecoVenda = null,
        string? documentoReferencia = null) =>
        new(
            empresaId,
            itemEstoqueId,
            quantidadeAdicional,
            novoCustoUnitario,
            novoPrecoVenda,
            new DateTime(2026, 4, 3, 10, 0, 0, DateTimeKind.Utc),
            null, null, null, null,
            documentoReferencia,
            null,
            null);

    [Fact]
    public async Task EmpresaId_vazio_lanca_UseCaseValidationException()
    {
        var useCase = BuildUseCase(out _, out _, out _, out _);

        var act = () => useCase.ExecuteAsync(CommandPadrao(Guid.Empty, Guid.NewGuid()));

        (await act.Should().ThrowAsync<UseCaseValidationException>())
            .WithMessage("*EmpresaId*");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    public async Task QuantidadeAdicional_zero_ou_negativa_lanca_QuantidadeInvalida(int quantidade)
    {
        var useCase = BuildUseCase(out _, out _, out _, out _);

        var act = () => useCase.ExecuteAsync(CommandPadrao(Guid.NewGuid(), Guid.NewGuid(), quantidade));

        await act.Should().ThrowAsync<QuantidadeInvalidaException>();
    }

    [Fact]
    public async Task NovoCustoUnitario_negativo_lanca_UseCaseValidationException()
    {
        var useCase = BuildUseCase(out _, out _, out _, out _);

        var act = () => useCase.ExecuteAsync(CommandPadrao(Guid.NewGuid(), Guid.NewGuid(), novoCustoUnitario: -10m));

        (await act.Should().ThrowAsync<UseCaseValidationException>())
            .WithMessage("*Custo*negativo*");
    }

    [Fact]
    public async Task NovoPrecoVendaSugerido_negativo_lanca_UseCaseValidationException()
    {
        var useCase = BuildUseCase(out _, out _, out _, out _);

        var act = () => useCase.ExecuteAsync(CommandPadrao(Guid.NewGuid(), Guid.NewGuid(), novoPrecoVenda: -5m));

        (await act.Should().ThrowAsync<UseCaseValidationException>())
            .WithMessage("*venda*negativo*");
    }

    [Fact]
    public async Task Item_nao_encontrado_lanca_UseCaseValidationException()
    {
        var useCase = BuildUseCase(out _, out var itemRepo, out _, out _);
        itemRepo.GetByIdAsync(Arg.Any<Guid>()).Returns((ItemEstoque?)null);

        var act = () => useCase.ExecuteAsync(CommandPadrao(Guid.NewGuid(), Guid.NewGuid()));

        (await act.Should().ThrowAsync<UseCaseValidationException>())
            .WithMessage("*Item de estoque nao encontrado*");
    }

    [Fact]
    public async Task Produto_nao_encontrado_lanca_UseCaseValidationException()
    {
        var useCase = BuildUseCase(out var produtoRepo, out var itemRepo, out _, out _);
        var empresaId = Guid.NewGuid();

        var item = new ItemEstoque
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            ProdutoId = Guid.NewGuid(),
            QuantidadeAtual = Quantidade.From(5),
            QuantidadeInicial = Quantidade.From(5),
            CustoUnitario = Dinheiro.FromDecimal(100m),
            Status = StatusItemEstoque.Ok,
            EntradaEm = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc)
        };
        itemRepo.GetByIdAsync(item.Id).Returns(item);
        produtoRepo.GetByIdAsync(item.ProdutoId).Returns((Produto?)null);

        var act = () => useCase.ExecuteAsync(CommandPadrao(empresaId, item.Id));

        (await act.Should().ThrowAsync<UseCaseValidationException>())
            .WithMessage("*Produto*nao encontrado*");
    }

    [Fact]
    public async Task Produto_de_outra_empresa_lanca_UseCaseValidationException()
    {
        var useCase = BuildUseCase(out var produtoRepo, out var itemRepo, out _, out _);
        var empresaId = Guid.NewGuid();
        var outraEmpresa = Guid.NewGuid();

        var produto = new Produto
        {
            Id = Guid.NewGuid(),
            EmpresaId = outraEmpresa,
            Nome = "Galaxy Buds",
            Status = StatusProduto.Ativo
        };

        var item = new ItemEstoque
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            ProdutoId = produto.Id,
            QuantidadeAtual = Quantidade.From(5),
            QuantidadeInicial = Quantidade.From(5),
            CustoUnitario = Dinheiro.FromDecimal(100m),
            Status = StatusItemEstoque.Ok,
            EntradaEm = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc)
        };

        itemRepo.GetByIdAsync(item.Id).Returns(item);
        produtoRepo.GetByIdAsync(produto.Id).Returns(produto);

        var act = () => useCase.ExecuteAsync(CommandPadrao(empresaId, item.Id));

        (await act.Should().ThrowAsync<UseCaseValidationException>())
            .WithMessage("*nao pertence a empresa*");
    }

    [Fact]
    public async Task DocumentoReferencia_e_propagado_para_MovimentacaoEstoque()
    {
        var useCase = BuildUseCase(out var produtoRepo, out var itemRepo, out var movRepo, out _);
        var empresaId = Guid.NewGuid();

        var produto = new Produto
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            Nome = "Galaxy Buds",
            Status = StatusProduto.Ativo
        };

        var item = new ItemEstoque
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            ProdutoId = produto.Id,
            QuantidadeAtual = Quantidade.From(5),
            QuantidadeInicial = Quantidade.From(5),
            CustoUnitario = Dinheiro.FromDecimal(100m),
            Status = StatusItemEstoque.Ok,
            EntradaEm = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc)
        };

        itemRepo.GetByIdAsync(item.Id).Returns(item);
        produtoRepo.GetByIdAsync(produto.Id).Returns(produto);

        var docRef = $"{Guid.NewGuid()}:{Guid.NewGuid()}";

        await useCase.ExecuteAsync(CommandPadrao(empresaId, item.Id, documentoReferencia: docRef));

        await movRepo.Received(1).InsertAsync(Arg.Is<MovimentacaoEstoque>(m =>
            m.DocumentoReferencia == docRef &&
            m.Natureza == NaturezaMovimentacaoEstoque.Reposicao));
    }
}
