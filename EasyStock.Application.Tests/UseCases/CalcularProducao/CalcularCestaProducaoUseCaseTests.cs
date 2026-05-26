using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.CalcularProducao;
using EasyStock.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.Tests.UseCases.CalcularProducao;

public class CalcularCestaProducaoUseCaseTests
{
    private readonly IProdutoComposicaoRepository _composicaoRepo = Substitute.For<IProdutoComposicaoRepository>();
    private readonly IItemEstoqueRepository _itemEstoqueRepo = Substitute.For<IItemEstoqueRepository>();
    private readonly ILogger<CalcularCestaProducaoUseCase> _logger = Substitute.For<ILogger<CalcularCestaProducaoUseCase>>();

    private CalcularCestaProducaoUseCase Build() => new(_composicaoRepo, _itemEstoqueRepo, _logger);

    private static Produto BuildProduto(Guid empresaId, string nome, decimal rendimento = 1m, UnidadeMedida rendimentoUnidade = UnidadeMedida.Un, UnidadeMedida baseUnidade = UnidadeMedida.Un)
        => new()
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            Nome = nome,
            CategoriaId = Guid.NewGuid(),
            RendimentoBase = rendimento,
            RendimentoUnidade = rendimentoUnidade,
            UnidadeMedidaBase = baseUnidade
        };

    private static ProdutoComposicao BuildComposicao(Produto produtoFinal, Produto insumo, decimal qtd, UnidadeMedida unidade)
        => new()
        {
            Id = Guid.NewGuid(),
            EmpresaId = produtoFinal.EmpresaId,
            ProdutoFinalId = produtoFinal.Id,
            InsumoId = insumo.Id,
            Quantidade = qtd,
            Unidade = unidade,
            ProdutoFinal = produtoFinal,
            Insumo = insumo
        };

    [Fact]
    public async Task EmpresaId_vazio_lanca()
    {
        var uc = Build();
        await Assert.ThrowsAsync<UseCaseValidationException>(() => uc.ExecuteAsync(
            new CalcularCestaProducaoCommand(Guid.Empty, null, new List<ItemCestaInput>
            {
                new(Guid.NewGuid(), 1m, UnidadeMedida.Un)
            })));
    }

    [Fact]
    public async Task Cesta_vazia_retorna_resultado_vazio_disponivel()
    {
        var uc = Build();
        var result = await uc.ExecuteAsync(
            new CalcularCestaProducaoCommand(Guid.NewGuid(), null, Array.Empty<ItemCestaInput>()));

        result.Itens.Should().BeEmpty();
        result.Consolidado.Should().BeEmpty();
        result.TudoDisponivel.Should().BeTrue();
        result.CustoEstimadoTotal.Should().BeNull();
    }

    [Fact]
    public async Task Quantidade_abaixo_do_minimo_lanca_INVALID_QUANTITY()
    {
        var uc = Build();
        var ex = await Assert.ThrowsAsync<UseCaseValidationException>(() => uc.ExecuteAsync(
            new CalcularCestaProducaoCommand(Guid.NewGuid(), null, new List<ItemCestaInput>
            {
                new(Guid.NewGuid(), 0.00005m, UnidadeMedida.Un)
            })));
        ex.Code.Should().Be("INVALID_QUANTITY");
    }

    [Fact]
    public async Task Item_sem_receita_vira_SemReceita_sem_derrubar_cesta()
    {
        var empresaId = Guid.NewGuid();
        var produtoComReceita = BuildProduto(empresaId, "Talharim", rendimento: 1m);
        var produtoSemReceita = Guid.NewGuid();

        var farinha = BuildProduto(empresaId, "Farinha", baseUnidade: UnidadeMedida.Kg);
        var comp = BuildComposicao(produtoComReceita, farinha, 0.2m, UnidadeMedida.Kg);

        _composicaoRepo.GetByProdutosFinaisAsync(empresaId, Arg.Any<IReadOnlyList<Guid>>(), null, Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, IReadOnlyCollection<ProdutoComposicao>>
            {
                [produtoComReceita.Id] = new[] { comp }
                // produtoSemReceita.Id NAO esta no dicionario
            });
        _itemEstoqueRepo.GetByProdutosAsync(empresaId, Arg.Any<IEnumerable<Guid>>(), null, Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, IReadOnlyCollection<ItemEstoque>>());

        var uc = Build();
        var result = await uc.ExecuteAsync(new CalcularCestaProducaoCommand(empresaId, null, new List<ItemCestaInput>
        {
            new(produtoComReceita.Id, 1m, UnidadeMedida.Un),
            new(produtoSemReceita, 2m, UnidadeMedida.Un)
        }));

        result.Itens.Should().HaveCount(2);
        result.Itens.Single(i => i.Status == ItemCestaStatus.Ok).ProdutoFinalId.Should().Be(produtoComReceita.Id);
        result.Itens.Single(i => i.Status == ItemCestaStatus.SemReceita).ProdutoFinalId.Should().Be(produtoSemReceita);
        result.Itens.Single(i => i.Status == ItemCestaStatus.SemReceita).Resultado.Should().BeNull();
    }

    [Fact]
    public async Task Dois_itens_com_mesmo_insumo_consolidam_soma()
    {
        var empresaId = Guid.NewGuid();
        var talharim = BuildProduto(empresaId, "Talharim", rendimento: 1m);
        var pao = BuildProduto(empresaId, "Pao", rendimento: 1m);
        var farinha = BuildProduto(empresaId, "Farinha", baseUnidade: UnidadeMedida.Kg);

        var compTalharim = BuildComposicao(talharim, farinha, 0.2m, UnidadeMedida.Kg);
        var compPao = BuildComposicao(pao, farinha, 0.5m, UnidadeMedida.Kg);

        _composicaoRepo.GetByProdutosFinaisAsync(empresaId, Arg.Any<IReadOnlyList<Guid>>(), null, Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, IReadOnlyCollection<ProdutoComposicao>>
            {
                [talharim.Id] = new[] { compTalharim },
                [pao.Id] = new[] { compPao }
            });
        _itemEstoqueRepo.GetByProdutosAsync(empresaId, Arg.Any<IEnumerable<Guid>>(), null, Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, IReadOnlyCollection<ItemEstoque>>());

        var uc = Build();
        var result = await uc.ExecuteAsync(new CalcularCestaProducaoCommand(empresaId, null, new List<ItemCestaInput>
        {
            new(talharim.Id, 1m, UnidadeMedida.Un),
            new(pao.Id, 1m, UnidadeMedida.Un)
        }));

        result.Consolidado.Should().HaveCount(1);
        result.Consolidado[0].InsumoId.Should().Be(farinha.Id);
        result.Consolidado[0].Precisa.Should().Be(0.7m); // 0.2 + 0.5
    }

    [Fact]
    public async Task Mesmo_insumo_em_unidades_compativeis_consolida_convertendo()
    {
        var empresaId = Guid.NewGuid();
        var talharim = BuildProduto(empresaId, "Talharim", rendimento: 1m);
        var pao = BuildProduto(empresaId, "Pao", rendimento: 1m);
        var farinha = BuildProduto(empresaId, "Farinha", baseUnidade: UnidadeMedida.Kg);

        var compTalharim = BuildComposicao(talharim, farinha, 0.2m, UnidadeMedida.Kg); // 0.2 kg
        var compPao = BuildComposicao(pao, farinha, 500m, UnidadeMedida.G); // 500 g = 0.5 kg

        _composicaoRepo.GetByProdutosFinaisAsync(empresaId, Arg.Any<IReadOnlyList<Guid>>(), null, Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, IReadOnlyCollection<ProdutoComposicao>>
            {
                [talharim.Id] = new[] { compTalharim },
                [pao.Id] = new[] { compPao }
            });
        _itemEstoqueRepo.GetByProdutosAsync(empresaId, Arg.Any<IEnumerable<Guid>>(), null, Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, IReadOnlyCollection<ItemEstoque>>());

        var uc = Build();
        var result = await uc.ExecuteAsync(new CalcularCestaProducaoCommand(empresaId, null, new List<ItemCestaInput>
        {
            new(talharim.Id, 1m, UnidadeMedida.Un),
            new(pao.Id, 1m, UnidadeMedida.Un)
        }));

        result.Consolidado.Should().HaveCount(1);
        // Builder usa a unidade do primeiro encontrado: Kg
        result.Consolidado[0].UnidadeReceita.Should().Be(UnidadeMedida.Kg);
        result.Consolidado[0].Precisa.Should().Be(0.7m); // 0.2 kg + 0.5 kg
    }

    [Fact]
    public async Task Mesmo_insumo_em_unidades_incompativeis_marca_consolidado_falha()
    {
        var empresaId = Guid.NewGuid();
        var talharim = BuildProduto(empresaId, "Talharim", rendimento: 1m);
        var pao = BuildProduto(empresaId, "Pao", rendimento: 1m);
        var farinha = BuildProduto(empresaId, "Farinha", baseUnidade: UnidadeMedida.Kg);

        // Talharim usa Farinha em Kg (massa); Pao usa Farinha em L (volume) — grupos diferentes
        var compTalharim = BuildComposicao(talharim, farinha, 0.2m, UnidadeMedida.Kg);
        var compPao = BuildComposicao(pao, farinha, 0.5m, UnidadeMedida.L);

        _composicaoRepo.GetByProdutosFinaisAsync(empresaId, Arg.Any<IReadOnlyList<Guid>>(), null, Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, IReadOnlyCollection<ProdutoComposicao>>
            {
                [talharim.Id] = new[] { compTalharim },
                [pao.Id] = new[] { compPao }
            });
        _itemEstoqueRepo.GetByProdutosAsync(empresaId, Arg.Any<IEnumerable<Guid>>(), null, Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, IReadOnlyCollection<ItemEstoque>>());

        var uc = Build();
        var result = await uc.ExecuteAsync(new CalcularCestaProducaoCommand(empresaId, null, new List<ItemCestaInput>
        {
            new(talharim.Id, 1m, UnidadeMedida.Un),
            new(pao.Id, 1m, UnidadeMedida.Un)
        }));

        result.Consolidado.Should().HaveCount(1);
        result.Consolidado[0].ConversaoFalhou.Should().BeTrue();
        result.Consolidado[0].Aviso.Should().NotBeNull();
        result.TudoDisponivel.Should().BeFalse();
    }

    [Fact]
    public async Task Item_com_rendimento_invalido_vira_Status_Erro_sem_derrubar_resto()
    {
        var empresaId = Guid.NewGuid();
        var bom = BuildProduto(empresaId, "Bom", rendimento: 1m);
        var quebrado = BuildProduto(empresaId, "Quebrado", rendimento: 0m); // invalido
        var farinha = BuildProduto(empresaId, "Farinha", baseUnidade: UnidadeMedida.Kg);

        var compBom = BuildComposicao(bom, farinha, 0.2m, UnidadeMedida.Kg);
        var compQuebrado = BuildComposicao(quebrado, farinha, 0.5m, UnidadeMedida.Kg);

        _composicaoRepo.GetByProdutosFinaisAsync(empresaId, Arg.Any<IReadOnlyList<Guid>>(), null, Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, IReadOnlyCollection<ProdutoComposicao>>
            {
                [bom.Id] = new[] { compBom },
                [quebrado.Id] = new[] { compQuebrado }
            });
        _itemEstoqueRepo.GetByProdutosAsync(empresaId, Arg.Any<IEnumerable<Guid>>(), null, Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, IReadOnlyCollection<ItemEstoque>>());

        var uc = Build();
        var result = await uc.ExecuteAsync(new CalcularCestaProducaoCommand(empresaId, null, new List<ItemCestaInput>
        {
            new(bom.Id, 1m, UnidadeMedida.Un),
            new(quebrado.Id, 1m, UnidadeMedida.Un)
        }));

        result.Itens.Should().HaveCount(2);
        result.Itens.Single(i => i.Status == ItemCestaStatus.Ok).ProdutoFinalId.Should().Be(bom.Id);
        result.Itens.Single(i => i.Status == ItemCestaStatus.Erro).ProdutoFinalId.Should().Be(quebrado.Id);
        result.Itens.Single(i => i.Status == ItemCestaStatus.Erro).Erro.Should().NotBeNullOrEmpty();
        result.TudoDisponivel.Should().BeFalse();
    }

    [Fact]
    public async Task Tudo_disponivel_quando_saldo_cobre_consolidado()
    {
        var empresaId = Guid.NewGuid();
        var talharim = BuildProduto(empresaId, "Talharim", rendimento: 1m);
        var farinha = BuildProduto(empresaId, "Farinha", baseUnidade: UnidadeMedida.Kg);
        var comp = BuildComposicao(talharim, farinha, 0.2m, UnidadeMedida.Kg);

        // Saldo de 10kg de farinha — cobre os 0.2kg necessarios
        var lote = new ItemEstoque
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            ProdutoId = farinha.Id,
            QuantidadeAtual = Quantidade.From(10m),
            EntradaEm = DateTime.UtcNow,
            CustoUnitario = Dinheiro.FromDecimal(5m)
        };

        _composicaoRepo.GetByProdutosFinaisAsync(empresaId, Arg.Any<IReadOnlyList<Guid>>(), null, Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, IReadOnlyCollection<ProdutoComposicao>>
            {
                [talharim.Id] = new[] { comp }
            });
        _itemEstoqueRepo.GetByProdutosAsync(empresaId, Arg.Any<IEnumerable<Guid>>(), null, Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, IReadOnlyCollection<ItemEstoque>>
            {
                [farinha.Id] = new[] { lote }
            });

        var uc = Build();
        var result = await uc.ExecuteAsync(new CalcularCestaProducaoCommand(empresaId, null, new List<ItemCestaInput>
        {
            new(talharim.Id, 1m, UnidadeMedida.Un)
        }));

        result.TudoDisponivel.Should().BeTrue();
        result.Consolidado[0].Falta.Should().BeNull();
        result.CustoEstimadoTotal.Should().Be(1m); // 0.2 kg * R$5/kg
    }

    [Fact]
    public async Task Batch_query_receitas_chamado_1x_para_todos_produtos()
    {
        var empresaId = Guid.NewGuid();
        var produtos = Enumerable.Range(0, 4).Select(i => BuildProduto(empresaId, $"P{i}")).ToList();
        var farinha = BuildProduto(empresaId, "Farinha", baseUnidade: UnidadeMedida.Kg);

        var composicoesPorProduto = produtos.ToDictionary(
            p => p.Id,
            p => (IReadOnlyCollection<ProdutoComposicao>)new[] { BuildComposicao(p, farinha, 0.1m, UnidadeMedida.Kg) });

        _composicaoRepo.GetByProdutosFinaisAsync(empresaId, Arg.Any<IReadOnlyList<Guid>>(), null, Arg.Any<CancellationToken>())
            .Returns(composicoesPorProduto);
        _itemEstoqueRepo.GetByProdutosAsync(empresaId, Arg.Any<IEnumerable<Guid>>(), null, Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, IReadOnlyCollection<ItemEstoque>>());

        var uc = Build();
        await uc.ExecuteAsync(new CalcularCestaProducaoCommand(empresaId, null,
            produtos.Select(p => new ItemCestaInput(p.Id, 1m, UnidadeMedida.Un)).ToList()));

        await _composicaoRepo.Received(1).GetByProdutosFinaisAsync(
            empresaId, Arg.Any<IReadOnlyList<Guid>>(), null, Arg.Any<CancellationToken>());
        await _itemEstoqueRepo.Received(1).GetByProdutosAsync(
            empresaId, Arg.Any<IEnumerable<Guid>>(), null, Arg.Any<CancellationToken>());
    }
}
