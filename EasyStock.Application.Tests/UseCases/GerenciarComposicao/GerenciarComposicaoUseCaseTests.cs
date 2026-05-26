using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.GerenciarComposicao;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.Tests.UseCases.GerenciarComposicao;

public class GerenciarComposicaoUseCaseTests
{
    private readonly IProdutoRepository _produtoRepo = Substitute.For<IProdutoRepository>();
    private readonly IProdutoComposicaoRepository _composicaoRepo = Substitute.For<IProdutoComposicaoRepository>();
    private readonly IProdutoComposicaoAlteracaoRepository _alteracaoRepo = Substitute.For<IProdutoComposicaoAlteracaoRepository>();
    private readonly ILojaRepository _lojaRepo = Substitute.For<ILojaRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly ILogger<GerenciarComposicaoUseCase> _logger = Substitute.For<ILogger<GerenciarComposicaoUseCase>>();

    private GerenciarComposicaoUseCase Build()
    {
        // Mock para ExecuteInTransactionAsync apenas invoca o action passado
        _uow.ExecuteInTransactionAsync(Arg.Any<Func<CancellationToken, Task>>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var action = callInfo.Arg<Func<CancellationToken, Task>>();
                return action(CancellationToken.None);
            });

        return new GerenciarComposicaoUseCase(
            _produtoRepo, _composicaoRepo, _alteracaoRepo, _lojaRepo, _uow, _logger);
    }

    private Produto BuildProduto(Guid empresaId, string nome = "Macarrao")
        => new()
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            Nome = nome,
            CategoriaId = Guid.NewGuid(),
            RendimentoBase = 1m,
            RendimentoUnidade = UnidadeMedida.Un,
            UnidadeMedidaBase = UnidadeMedida.Un
        };

    [Fact]
    public async Task SubstituirAsync_com_quantidade_abaixo_da_precisao_lanca_INVALID_QUANTITY()
    {
        var empresaId = Guid.NewGuid();
        var produto = BuildProduto(empresaId);
        _produtoRepo.GetByIdAsync(empresaId, produto.Id).Returns(produto);

        var insumoId = Guid.NewGuid();
        var insumo = BuildProduto(empresaId, "Farinha");
        insumo.Id = insumoId;
        _produtoRepo.GetByIdAsync(empresaId, insumoId).Returns(insumo);

        var uc = Build();
        var command = new SubstituirComposicaoCommand(
            empresaId, produto.Id, null, Guid.NewGuid(),
            1m, UnidadeMedida.Un, UnidadeMedida.Un,
            [new ComposicaoLinhaInput(insumoId, 0.00005m, UnidadeMedida.Kg, null, 0)],
            null);

        var ex = await Assert.ThrowsAsync<UseCaseValidationException>(() => uc.SubstituirAsync(command));
        ex.Code.Should().Be("INVALID_QUANTITY");
    }

    [Fact]
    public async Task SubstituirAsync_com_produto_como_proprio_insumo_lanca_CYCLE_DETECTED()
    {
        var empresaId = Guid.NewGuid();
        var produto = BuildProduto(empresaId);
        _produtoRepo.GetByIdAsync(empresaId, produto.Id).Returns(produto);

        var uc = Build();
        var command = new SubstituirComposicaoCommand(
            empresaId, produto.Id, null, Guid.NewGuid(),
            1m, UnidadeMedida.Un, UnidadeMedida.Un,
            [new ComposicaoLinhaInput(produto.Id, 1m, UnidadeMedida.Kg, null, 0)],
            null);

        var ex = await Assert.ThrowsAsync<UseCaseValidationException>(() => uc.SubstituirAsync(command));
        ex.Code.Should().Be("CYCLE_DETECTED");
    }

    [Fact]
    public async Task SubstituirAsync_com_insumo_duplicado_lanca_DUPLICATE_INSUMO()
    {
        var empresaId = Guid.NewGuid();
        var produto = BuildProduto(empresaId);
        _produtoRepo.GetByIdAsync(empresaId, produto.Id).Returns(produto);

        var insumoId = Guid.NewGuid();
        var insumo = BuildProduto(empresaId, "Farinha");
        insumo.Id = insumoId;
        _produtoRepo.GetByIdAsync(empresaId, insumoId).Returns(insumo);

        var uc = Build();
        var command = new SubstituirComposicaoCommand(
            empresaId, produto.Id, null, Guid.NewGuid(),
            1m, UnidadeMedida.Un, UnidadeMedida.Un,
            [
                new ComposicaoLinhaInput(insumoId, 1m, UnidadeMedida.Kg, null, 0),
                new ComposicaoLinhaInput(insumoId, 2m, UnidadeMedida.Kg, null, 1)
            ],
            null);

        var ex = await Assert.ThrowsAsync<UseCaseValidationException>(() => uc.SubstituirAsync(command));
        ex.Code.Should().Be("DUPLICATE_INSUMO");
    }

    [Fact]
    public async Task SubstituirAsync_com_insumo_de_outra_empresa_lanca_CROSS_TENANT_INSUMO()
    {
        var empresaId = Guid.NewGuid();
        var produto = BuildProduto(empresaId);
        _produtoRepo.GetByIdAsync(empresaId, produto.Id).Returns(produto);

        var insumoId = Guid.NewGuid();
        _produtoRepo.GetByIdAsync(empresaId, insumoId).Returns((Produto?)null);  // nao pertence

        var uc = Build();
        var command = new SubstituirComposicaoCommand(
            empresaId, produto.Id, null, Guid.NewGuid(),
            1m, UnidadeMedida.Un, UnidadeMedida.Un,
            [new ComposicaoLinhaInput(insumoId, 1m, UnidadeMedida.Kg, null, 0)],
            null);

        var ex = await Assert.ThrowsAsync<UseCaseValidationException>(() => uc.SubstituirAsync(command));
        ex.Code.Should().Be("CROSS_TENANT_INSUMO");
    }

    [Fact]
    public async Task SubstituirAsync_com_rendimento_zero_lanca_INVALID_RENDIMENTO()
    {
        var empresaId = Guid.NewGuid();

        var uc = Build();
        var command = new SubstituirComposicaoCommand(
            empresaId, Guid.NewGuid(), null, Guid.NewGuid(),
            0m, UnidadeMedida.Un, UnidadeMedida.Un,
            [],
            null);

        var ex = await Assert.ThrowsAsync<UseCaseValidationException>(() => uc.SubstituirAsync(command));
        ex.Code.Should().Be("INVALID_RENDIMENTO");
    }
}
