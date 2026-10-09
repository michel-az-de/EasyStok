using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.UseCases.Atendimento.Producao;
using EasyStock.Domain.Entities.Storefront;
using EasyStock.Domain.ValueObjects;
using StorefrontEntity = EasyStock.Domain.Entities.Storefront.Storefront;

namespace EasyStock.Application.Tests.UseCases.Atendimento.Producao;

/// <summary>M2.4a (#1498): receita por prato com o custo por porção, convertendo a unidade da receita.</summary>
public class ReceitasDaProducaoUseCaseTests
{
    private static readonly Guid EmpresaId = Guid.NewGuid();
    private readonly StorefrontEntity _vitrine = StorefrontEntity.Criar(EmpresaId, "casa-da-baba", "Casa da Baba", 0m);
    private readonly IStorefrontRepository _storefronts = Substitute.For<IStorefrontRepository>();
    private readonly ICardapioItemRepository _cardapio = Substitute.For<ICardapioItemRepository>();
    private readonly IProdutoRepository _produtos = Substitute.For<IProdutoRepository>();
    private readonly IProdutoComposicaoRepository _composicao = Substitute.For<IProdutoComposicaoRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    public ReceitasDaProducaoUseCaseTests()
    {
        _vitrine.Ativar();
        _storefronts.GetByEmpresaAsync(EmpresaId, Arg.Any<CancellationToken>()).Returns(_vitrine);
    }

    private ReceitasDaProducaoUseCase Sut() => new(_storefronts, _cardapio, _produtos, _composicao, _uow);

    private Produto Prato(decimal rendimento)
    {
        var p = new Produto { Id = Guid.NewGuid(), EmpresaId = EmpresaId, Nome = "Lasanha", RendimentoBase = rendimento, RendimentoUnidade = UnidadeMedida.Un };
        _produtos.GetByIdAsync(EmpresaId, p.Id).Returns(p);
        return p;
    }

    private static Produto Insumo(string nome, UnidadeMedida unidade, decimal? custo) => new()
    {
        Id = Guid.NewGuid(), EmpresaId = EmpresaId, Nome = nome, UnidadeMedidaBase = unidade, EhInsumo = true,
        CustoReferencia = custo is null ? null : Dinheiro.FromDecimal(custo.Value),
    };

    private void Receita(Produto prato, params (Produto Insumo, decimal Qtd, UnidadeMedida Unidade)[] linhas) =>
        _composicao.GetByProdutoFinalAsync(EmpresaId, prato.Id, null, Arg.Any<CancellationToken>()).Returns(
            linhas.Select((l, i) => new ProdutoComposicao
            {
                Id = Guid.NewGuid(), EmpresaId = EmpresaId, ProdutoFinalId = prato.Id, InsumoId = l.Insumo.Id,
                Insumo = l.Insumo, Quantidade = l.Qtd, Unidade = l.Unidade, OrdemExibicao = i,
            }).ToList());

    [Fact]
    public async Task CustoPorPorcao_SomaAsLinhasConvertidas_EDividePeloRendimento()
    {
        // Rende 6 porções: 1,2 kg de molho a R$ 0,03/g (R$ 36) + 6 bandejas a R$ 1,50 (R$ 9) = R$ 45 → R$ 7,50 por porção.
        var lasanha = Prato(6);
        Receita(lasanha, (Insumo("Molho", UnidadeMedida.G, 0.03m), 1.2m, UnidadeMedida.Kg), (Insumo("Bandeja", UnidadeMedida.Un, 1.5m), 6, UnidadeMedida.Un));

        var r = await Sut().ObterAsync(EmpresaId, lasanha.Id);

        r.Linhas.Select(l => l.Custo).Should().Equal(36m, 9m);
        r.CustoTotal.Should().Be(45m);
        r.CustoPorRendimento.Should().Be(7.5m);
    }

    [Fact]
    public async Task InsumoSemCusto_NaoFechaOCusto()
    {
        var lasanha = Prato(6);
        Receita(lasanha, (Insumo("Molho", UnidadeMedida.G, 0.03m), 1000, UnidadeMedida.G), (Insumo("Recheio", UnidadeMedida.G, null), 500, UnidadeMedida.G));

        var r = await Sut().ObterAsync(EmpresaId, lasanha.Id);

        r.CustoTotal.Should().BeNull("custo parcial pareceria barato demais");
        r.Linhas[0].Custo.Should().Be(30m);
    }

    [Fact]
    public async Task Listar_SoPratosLigadosAoEstoque()
    {
        var lasanha = Prato(6);
        Receita(lasanha);
        var p = (Produto)Activator.CreateInstance(typeof(Produto), nonPublic: true)!;
        typeof(Produto).GetProperty("Id")!.SetValue(p, lasanha.Id);
        var ligado = CardapioItem.CriarAPartirDeProduto(_vitrine.Id, p);
        var avulso = CardapioItem.CriarAvulso(_vitrine.Id, "Bolo", 20m);
        _cardapio.GetTodosDoStorefrontAsync(_vitrine.Id, Arg.Any<CancellationToken>()).Returns([ligado, avulso]);

        var lista = await Sut().ListarAsync(EmpresaId);

        lista.Should().ContainSingle().Which.ProdutoId.Should().Be(lasanha.Id);
    }

    [Fact]
    public async Task MarcarBaixaAutomatica_LigaEDesliga_EGrava()
    {
        var lasanha = Prato(6);
        Receita(lasanha, (Insumo("Molho", UnidadeMedida.G, 0.03m), 600, UnidadeMedida.G));

        await Sut().MarcarBaixaAutomaticaAsync(EmpresaId, lasanha.Id, true);
        lasanha.BaixaInsumoAutomatica.Should().BeTrue();
        (await Sut().ObterAsync(EmpresaId, lasanha.Id)).BaixaAutomatica.Should().BeTrue();

        await Sut().MarcarBaixaAutomaticaAsync(EmpresaId, lasanha.Id, false);
        lasanha.BaixaInsumoAutomatica.Should().BeFalse();
        await _produtos.Received(2).UpdateAsync(lasanha);
        await _uow.Received(2).CommitAsync();
    }

    [Fact]
    public async Task LigarBaixaSemReceita_Recusa()
    {
        var lasanha = Prato(6);
        Receita(lasanha);

        var act = () => Sut().MarcarBaixaAutomaticaAsync(EmpresaId, lasanha.Id, true);

        await act.Should().ThrowAsync<UseCaseValidationException>().WithMessage("*receita*");
        lasanha.BaixaInsumoAutomatica.Should().BeFalse();
        await _uow.DidNotReceive().CommitAsync();
    }
}
