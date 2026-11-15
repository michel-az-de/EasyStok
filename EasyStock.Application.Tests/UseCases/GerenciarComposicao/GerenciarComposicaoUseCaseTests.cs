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
        _produtoRepo.GetByIdParaAtualizarAsync(empresaId, produto.Id).Returns(produto);

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

    // #1776 (item 7): a receita aceitava insumo medido em Un com quantidade em G (PUT 204). A baixa e o custo
    // não convertem grupos diferentes, então a linha virava aviso eterno e custo desconhecido.
    [Theory]
    [InlineData(UnidadeMedida.Un, UnidadeMedida.G)]
    [InlineData(UnidadeMedida.Kg, UnidadeMedida.Ml)]
    [InlineData(UnidadeMedida.L, UnidadeMedida.Un)]
    public async Task SubstituirAsync_com_unidade_que_nao_converte_para_a_do_insumo_lanca_INCOMPATIBLE_UNIT(
        UnidadeMedida unidadeDoInsumo, UnidadeMedida unidadeDaReceita)
    {
        var empresaId = Guid.NewGuid();
        var produto = BuildProduto(empresaId);
        _produtoRepo.GetByIdParaAtualizarAsync(empresaId, produto.Id).Returns(produto);
        var insumo = BuildProduto(empresaId, "Ovo");
        insumo.UnidadeMedidaBase = unidadeDoInsumo;
        _produtoRepo.GetByIdAsync(empresaId, insumo.Id).Returns(insumo);

        var command = new SubstituirComposicaoCommand(
            empresaId, produto.Id, null, Guid.NewGuid(),
            1m, UnidadeMedida.Un, UnidadeMedida.Un,
            [new ComposicaoLinhaInput(insumo.Id, 50m, unidadeDaReceita, null, 0)],
            null);

        var ex = await Assert.ThrowsAsync<UseCaseValidationException>(() => Build().SubstituirAsync(command));
        ex.Code.Should().Be("INCOMPATIBLE_UNIT");
        ex.Message.Should().Contain("Ovo");
        await _composicaoRepo.DidNotReceiveWithAnyArgs().InsertAsync(default!, default);
    }

    [Fact]
    public async Task SubstituirAsync_com_unidade_do_mesmo_grupo_converte_e_grava()
    {
        var empresaId = Guid.NewGuid();
        var produto = BuildProduto(empresaId);
        _produtoRepo.GetByIdParaAtualizarAsync(empresaId, produto.Id).Returns(produto);
        var insumo = BuildProduto(empresaId, "Farinha");
        insumo.UnidadeMedidaBase = UnidadeMedida.Kg;
        _produtoRepo.GetByIdAsync(empresaId, insumo.Id).Returns(insumo);
        _composicaoRepo.GetByProdutoFinalAsync(empresaId, produto.Id, null, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<ProdutoComposicao>());

        await Build().SubstituirAsync(new SubstituirComposicaoCommand(
            empresaId, produto.Id, null, Guid.NewGuid(),
            1m, UnidadeMedida.Un, UnidadeMedida.Un,
            [new ComposicaoLinhaInput(insumo.Id, 300m, UnidadeMedida.G, null, 0)],
            null));

        await _composicaoRepo.Received(1).InsertAsync(Arg.Is<ProdutoComposicao>(c => c.Unidade == UnidadeMedida.G), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SubstituirAsync_com_produto_como_proprio_insumo_lanca_CYCLE_DETECTED()
    {
        var empresaId = Guid.NewGuid();
        var produto = BuildProduto(empresaId);
        _produtoRepo.GetByIdParaAtualizarAsync(empresaId, produto.Id).Returns(produto);

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
        _produtoRepo.GetByIdParaAtualizarAsync(empresaId, produto.Id).Returns(produto);

        var insumoId = Guid.NewGuid();
        var insumo = BuildProduto(empresaId, "Farinha");
        insumo.Id = insumoId;
        insumo.UnidadeMedidaBase = UnidadeMedida.Kg; // #1776 (item 7): a linha em Kg precisa de insumo de massa.
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
        _produtoRepo.GetByIdParaAtualizarAsync(empresaId, produto.Id).Returns(produto);

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
