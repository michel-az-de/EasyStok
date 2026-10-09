using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.UseCases.Atendimento.Producao;
using EasyStock.Application.UseCases.CalcularProducao;
using EasyStock.Domain.Entities.Storefront;
using EasyStock.Domain.ValueObjects;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using StorefrontEntity = EasyStock.Domain.Entities.Storefront.Storefront;

namespace EasyStock.Application.Tests.UseCases.Atendimento.Producao;

/// <summary>M2.5 (#1502): sugestão de produção (D-M2-05 = a) e planejamento pela cesta da calculadora.</summary>
public class PlanejamentoDaProducaoUseCaseTests
{
    private static readonly Guid EmpresaId = Guid.NewGuid();
    // 09/10/2026 12:00 em Brasília (15:00 UTC).
    private readonly FakeTimeProvider _relogio = new(new DateTimeOffset(2026, 10, 9, 15, 0, 0, TimeSpan.Zero));
    private readonly StorefrontEntity _vitrine = StorefrontEntity.Criar(EmpresaId, "casa-da-baba", "Casa da Baba", 0m);
    private readonly IStorefrontRepository _storefronts = Substitute.For<IStorefrontRepository>();
    private readonly ICardapioItemRepository _cardapio = Substitute.For<ICardapioItemRepository>();
    private readonly IProdutoRepository _produtos = Substitute.For<IProdutoRepository>();
    private readonly IItemEstoqueRepository _itens = Substitute.For<IItemEstoqueRepository>();
    private readonly IPedidoRepository _pedidos = Substitute.For<IPedidoRepository>();
    private readonly IProdutoComposicaoRepository _composicao = Substitute.For<IProdutoComposicaoRepository>();
    private readonly Dictionary<Guid, IReadOnlyCollection<ItemEstoque>> _lotes = [];
    private readonly List<CardapioItem> _pratos = [];

    public PlanejamentoDaProducaoUseCaseTests()
    {
        _vitrine.Ativar();
        _storefronts.GetByEmpresaAsync(EmpresaId, Arg.Any<CancellationToken>()).Returns(_vitrine);
        _cardapio.GetTodosDoStorefrontAsync(_vitrine.Id, Arg.Any<CancellationToken>()).Returns(_ => _pratos);
        _itens.GetByProdutosAsync(EmpresaId, Arg.Any<IEnumerable<Guid>>(), null, Arg.Any<CancellationToken>()).Returns(_ => _lotes);
        _pedidos.GetDemandaAgendadaAsync(EmpresaId, Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns([]);
    }

    private CalcularCestaProducaoUseCase Cesta() => new(_composicao, _itens, Substitute.For<ILogger<CalcularCestaProducaoUseCase>>());

    private PlanejamentoDaProducaoUseCase Sut() => new(_storefronts, _cardapio, _produtos, _itens, _pedidos, Cesta(), _relogio);

    private CardapioItem Prato(string nome, int? minimo, params decimal[] saldos)
    {
        var produto = new Produto { Id = Guid.NewGuid(), EmpresaId = EmpresaId, Nome = nome, QuantidadeMinima = minimo };
        _produtos.GetByIdAsync(EmpresaId, produto.Id).Returns(produto);
        _lotes[produto.Id] = saldos.Select(s => Lote(produto.Id, s)).ToList();
        var vazio = (Produto)Activator.CreateInstance(typeof(Produto), nonPublic: true)!;
        typeof(Produto).GetProperty("Id")!.SetValue(vazio, produto.Id);
        var item = CardapioItem.CriarAPartirDeProduto(_vitrine.Id, vazio);
        item.AtualizarMetadata(nomePublico: nome);
        _pratos.Add(item);
        return item;
    }

    private static ItemEstoque Lote(Guid produtoId, decimal atual, decimal descoberto = 0m) => new()
    {
        Id = Guid.NewGuid(), EmpresaId = EmpresaId, ProdutoId = produtoId,
        QuantidadeInicial = Quantidade.From(10), QuantidadeAtual = Quantidade.From(atual),
        QuantidadeDescoberta = Quantidade.From(descoberto), ValidadeEm = Validade.From(new DateTime(2026, 10, 20)),
        CustoUnitario = Dinheiro.FromDecimal(4m), Status = StatusItemEstoque.Ok,
        EntradaEm = DateTime.UtcNow.AddDays(-1), CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow,
    };

    [Fact]
    public async Task Minimo5_Saldo4_DoisAgendados_Sugere3()
    {
        // Aceite do plano (M2.5): mínimo 5, saldo 4 e 2 pedidos agendados para amanhã → 3 porções.
        var lasanha = Prato("Lasanha", 5, 4);
        _pedidos.GetDemandaAgendadaAsync(EmpresaId, Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns([new DemandaDePedido(lasanha.Id, lasanha.ProdutoId, 1), new DemandaDePedido(lasanha.Id, lasanha.ProdutoId, 1)]);

        var r = await Sut().SugestaoAsync(EmpresaId);

        r.Ate.Should().Be(new DateOnly(2026, 10, 10), "o padrão é amanhã no dia operacional");
        var s = r.Pratos.Should().ContainSingle().Subject;
        (s.Minimo, s.Saldo, s.Agendados, s.Sugestao).Should().Be((5m, 4m, 2m, 3m));
    }

    [Fact]
    public async Task SaldoQueCobreTudo_SugereZero_EDescobertoSoma()
    {
        Prato("Nhoque", 2, 10);
        var ravioli = Prato("Ravióli", 0);
        _lotes[ravioli.ProdutoId!.Value] = [Lote(ravioli.ProdutoId!.Value, 0, descoberto: 2)];

        var r = await Sut().SugestaoAsync(EmpresaId);

        r.Pratos.Select(p => (p.Nome, p.Sugestao)).Should().Equal(("nhoque", 0m), ("ravióli", 2m));
    }

    [Fact]
    public async Task DemandaPeloProduto_ContaQuandoOItemNaoTemPrato_EOutroPratoNaoConta()
    {
        var lasanha = Prato("Lasanha", 0);
        var outro = Prato("Canelone", 0);
        _pedidos.GetDemandaAgendadaAsync(EmpresaId, Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns([new DemandaDePedido(null, lasanha.ProdutoId, 3), new DemandaDePedido(outro.Id, outro.ProdutoId, 4)]);

        var r = await Sut().SugestaoAsync(EmpresaId);

        r.Pratos.Select(p => (p.Nome, p.Agendados)).Should().Equal(("lasanha", 3m), ("canelone", 4m));
    }

    [Fact]
    public async Task OCorteDosPedidosEOFimDoDiaPedido_EmBrasilia()
    {
        Prato("Lasanha", 0);

        await Sut().SugestaoAsync(EmpresaId, new DateOnly(2026, 10, 12));

        // Fim do dia 12/10 em Brasília = 13/10 00:00 BRT = 03:00 UTC.
        await _pedidos.Received(1).GetDemandaAgendadaAsync(EmpresaId, new DateTime(2026, 10, 13, 3, 0, 0, DateTimeKind.Utc), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DataQueJaPassou_Recusa()
    {
        var act = () => Sut().SugestaoAsync(EmpresaId, new DateOnly(2026, 10, 8));
        await act.Should().ThrowAsync<UseCaseValidationException>();
    }

    [Fact]
    public async Task Planejar_DaOMesmoResultadoQueACestaMobile()
    {
        // Paridade (aceite M2.5): a rota web e a calculadora mobile usam a mesma cesta.
        var lasanha = new Produto { Id = Guid.NewGuid(), EmpresaId = EmpresaId, Nome = "Lasanha", RendimentoBase = 6, RendimentoUnidade = UnidadeMedida.Un };
        var molho = new Produto { Id = Guid.NewGuid(), EmpresaId = EmpresaId, Nome = "Molho", UnidadeMedidaBase = UnidadeMedida.G, CustoReferencia = Dinheiro.FromDecimal(0.03m) };
        _composicao.GetByProdutosFinaisAsync(EmpresaId, Arg.Any<IReadOnlyList<Guid>>(), null, Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, IReadOnlyCollection<ProdutoComposicao>>
            {
                [lasanha.Id] = [new ProdutoComposicao
                {
                    Id = Guid.NewGuid(), EmpresaId = EmpresaId, ProdutoFinalId = lasanha.Id, InsumoId = molho.Id,
                    Quantidade = 1.2m, Unidade = UnidadeMedida.Kg, ProdutoFinal = lasanha, Insumo = molho,
                }],
            });

        var web = await Sut().PlanejarAsync(EmpresaId, [new PratoPlanejado(lasanha.Id, 8), new PratoPlanejado(lasanha.Id, 4)]);
        var mobile = await Cesta().ExecuteAsync(new CalcularCestaProducaoCommand(EmpresaId, null, [new ItemCestaInput(lasanha.Id, 12, UnidadeMedida.Un)]));

        web.Should().BeEquivalentTo(mobile);
        var insumo = web.Consolidado.Should().ContainSingle().Subject;
        insumo.Precisa.Should().Be(2.4m, "12 porções ÷ rende 6 × 1,2 kg");
        insumo.Falta.Should().Be(2.4m, "sem saldo de molho");
        insumo.CustoEstimado.Should().Be(72m);
    }

    [Fact]
    public async Task PlanejarSemPorcoes_Recusa()
    {
        var act = () => Sut().PlanejarAsync(EmpresaId, [new PratoPlanejado(Guid.NewGuid(), 0)]);
        await act.Should().ThrowAsync<UseCaseValidationException>();
    }
}
