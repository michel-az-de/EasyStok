using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.CalcularProducao;
using EasyStock.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.Tests.UseCases.CalcularProducao;

public class CalcularProducaoUseCaseTests
{
    private readonly IProdutoRepository _produtoRepo = Substitute.For<IProdutoRepository>();
    private readonly IProdutoComposicaoRepository _composicaoRepo = Substitute.For<IProdutoComposicaoRepository>();
    private readonly IItemEstoqueRepository _itemEstoqueRepo = Substitute.For<IItemEstoqueRepository>();
    private readonly ILogger<CalcularProducaoUseCase> _logger = Substitute.For<ILogger<CalcularProducaoUseCase>>();

    private CalcularProducaoUseCase Build() => new(_produtoRepo, _composicaoRepo, _itemEstoqueRepo, _logger);

    private Produto BuildProduto(Guid empresaId, string nome = "Macarrao bolonhesa", decimal rendimento = 50m, UnidadeMedida rendimentoUnidade = UnidadeMedida.Un)
        => new()
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            Nome = nome,
            CategoriaId = Guid.NewGuid(),
            RendimentoBase = rendimento,
            RendimentoUnidade = rendimentoUnidade,
            UnidadeMedidaBase = UnidadeMedida.Un
        };

    [Fact]
    public async Task EmpresaId_vazio_lanca()
    {
        var uc = Build();
        await Assert.ThrowsAsync<UseCaseValidationException>(() => uc.ExecuteAsync(
            new CalcularProducaoCommand(Guid.Empty, Guid.NewGuid(), 10m, UnidadeMedida.Un, null)));
    }

    [Fact]
    public async Task Quantidade_abaixo_da_precisao_minima_lanca_INVALID_QUANTITY()
    {
        var uc = Build();
        var ex = await Assert.ThrowsAsync<UseCaseValidationException>(() => uc.ExecuteAsync(
            new CalcularProducaoCommand(Guid.NewGuid(), Guid.NewGuid(), 0.00005m, UnidadeMedida.Un, null)));
        ex.Code.Should().Be("INVALID_QUANTITY");
    }

    [Fact]
    public async Task Produto_inexistente_lanca_RECIPE_NOT_FOUND()
    {
        var uc = Build();
        var empresaId = Guid.NewGuid();
        _produtoRepo.GetByIdAsync(empresaId, Arg.Any<Guid>()).Returns((Produto?)null);

        var ex = await Assert.ThrowsAsync<UseCaseValidationException>(() => uc.ExecuteAsync(
            new CalcularProducaoCommand(empresaId, Guid.NewGuid(), 10m, UnidadeMedida.Un, null)));
        ex.Code.Should().Be("RECIPE_NOT_FOUND");
    }

    [Fact]
    public async Task Produto_sem_receita_lanca_RECIPE_NOT_FOUND()
    {
        var empresaId = Guid.NewGuid();
        var produto = BuildProduto(empresaId);
        _produtoRepo.GetByIdAsync(empresaId, produto.Id).Returns(produto);
        _composicaoRepo.GetByProdutoFinalAsync(empresaId, produto.Id, null, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<ProdutoComposicao>());

        var uc = Build();
        var ex = await Assert.ThrowsAsync<UseCaseValidationException>(() => uc.ExecuteAsync(
            new CalcularProducaoCommand(empresaId, produto.Id, 10m, UnidadeMedida.Un, null)));
        ex.Code.Should().Be("RECIPE_NOT_FOUND");
    }

    [Fact]
    public async Task Calcular_50_para_25_divide_quantidades_pela_metade()
    {
        var empresaId = Guid.NewGuid();
        var produto = BuildProduto(empresaId, rendimento: 50m, rendimentoUnidade: UnidadeMedida.Un);

        var insumoFarinha = new Produto
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            Nome = "Farinha",
            CategoriaId = Guid.NewGuid(),
            UnidadeMedidaBase = UnidadeMedida.Kg,
            CustoReferencia = Dinheiro.FromDecimal(5m)
        };

        var composicao = new ProdutoComposicao
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            ProdutoFinalId = produto.Id,
            InsumoId = insumoFarinha.Id,
            Quantidade = 5m,
            Unidade = UnidadeMedida.Kg,
            Insumo = insumoFarinha
        };

        _produtoRepo.GetByIdAsync(empresaId, produto.Id).Returns(produto);
        _composicaoRepo.GetByProdutoFinalAsync(empresaId, produto.Id, null, Arg.Any<CancellationToken>())
            .Returns([composicao]);
        _itemEstoqueRepo.GetByProdutosAsync(empresaId, Arg.Any<IEnumerable<Guid>>(), null, Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, IReadOnlyCollection<ItemEstoque>>());

        var uc = Build();
        var result = await uc.ExecuteAsync(
            new CalcularProducaoCommand(empresaId, produto.Id, 25m, UnidadeMedida.Un, null));

        result.FatorMultiplicador.Should().Be(0.5m);
        result.Insumos.Should().HaveCount(1);
        result.Insumos[0].QuantidadeNecessaria.Should().Be(2.5m); // 5kg * 0.5 fator
    }

    [Fact]
    public async Task Insumo_sem_estoque_retorna_falta_igual_necessario()
    {
        var empresaId = Guid.NewGuid();
        var produto = BuildProduto(empresaId, rendimento: 10m);

        var insumo = new Produto
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            Nome = "Acucar",
            CategoriaId = Guid.NewGuid(),
            UnidadeMedidaBase = UnidadeMedida.Kg
        };

        var composicao = new ProdutoComposicao
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            ProdutoFinalId = produto.Id,
            InsumoId = insumo.Id,
            Quantidade = 1m,
            Unidade = UnidadeMedida.Kg,
            Insumo = insumo
        };

        _produtoRepo.GetByIdAsync(empresaId, produto.Id).Returns(produto);
        _composicaoRepo.GetByProdutoFinalAsync(empresaId, produto.Id, null, Arg.Any<CancellationToken>())
            .Returns([composicao]);
        _itemEstoqueRepo.GetByProdutosAsync(empresaId, Arg.Any<IEnumerable<Guid>>(), null, Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, IReadOnlyCollection<ItemEstoque>>());

        var uc = Build();
        var result = await uc.ExecuteAsync(
            new CalcularProducaoCommand(empresaId, produto.Id, 10m, UnidadeMedida.Un, null));

        result.TudoDisponivel.Should().BeFalse();
        result.Insumos[0].Falta.Should().Be(1m); // necessario integral
        result.Insumos[0].SaldoAtual.Should().Be(0m);
    }

    [Fact]
    public async Task Saldo_em_unidade_incompativel_marca_ConversaoFalhou()
    {
        var empresaId = Guid.NewGuid();
        var produto = BuildProduto(empresaId, rendimento: 1m);

        var insumo = new Produto
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            Nome = "Farinha",
            CategoriaId = Guid.NewGuid(),
            UnidadeMedidaBase = UnidadeMedida.Un  // estoque conta em Un, receita pede G
        };

        var composicao = new ProdutoComposicao
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            ProdutoFinalId = produto.Id,
            InsumoId = insumo.Id,
            Quantidade = 200m,
            Unidade = UnidadeMedida.G,
            Insumo = insumo
        };

        _produtoRepo.GetByIdAsync(empresaId, produto.Id).Returns(produto);
        _composicaoRepo.GetByProdutoFinalAsync(empresaId, produto.Id, null, Arg.Any<CancellationToken>())
            .Returns([composicao]);
        _itemEstoqueRepo.GetByProdutosAsync(empresaId, Arg.Any<IEnumerable<Guid>>(), null, Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, IReadOnlyCollection<ItemEstoque>>());

        var uc = Build();
        var result = await uc.ExecuteAsync(
            new CalcularProducaoCommand(empresaId, produto.Id, 1m, UnidadeMedida.Un, null));

        result.Insumos[0].ConversaoFalhou.Should().BeTrue();
        result.Insumos[0].Aviso.Should().NotBeNull();
        result.TudoDisponivel.Should().BeFalse();
    }

    [Fact]
    public async Task Batch_query_chamado_1x_para_todos_insumos()
    {
        var empresaId = Guid.NewGuid();
        var produto = BuildProduto(empresaId, rendimento: 1m);

        var insumos = Enumerable.Range(0, 5).Select(i => new Produto
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            Nome = $"Insumo {i}",
            CategoriaId = Guid.NewGuid(),
            UnidadeMedidaBase = UnidadeMedida.Kg
        }).ToList();

        var composicoes = insumos.Select(ins => new ProdutoComposicao
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            ProdutoFinalId = produto.Id,
            InsumoId = ins.Id,
            Quantidade = 1m,
            Unidade = UnidadeMedida.Kg,
            Insumo = ins
        }).ToList();

        _produtoRepo.GetByIdAsync(empresaId, produto.Id).Returns(produto);
        _composicaoRepo.GetByProdutoFinalAsync(empresaId, produto.Id, null, Arg.Any<CancellationToken>())
            .Returns(composicoes);
        _itemEstoqueRepo.GetByProdutosAsync(empresaId, Arg.Any<IEnumerable<Guid>>(), null, Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, IReadOnlyCollection<ItemEstoque>>());

        var uc = Build();
        await uc.ExecuteAsync(new CalcularProducaoCommand(empresaId, produto.Id, 1m, UnidadeMedida.Un, null));

        // Garante que batch query foi chamado EXATAMENTE 1 vez (nao N vezes por insumo)
        await _itemEstoqueRepo.Received(1).GetByProdutosAsync(
            empresaId, Arg.Any<IEnumerable<Guid>>(), null, Arg.Any<CancellationToken>());
    }
}
