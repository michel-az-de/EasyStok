using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.UseCases.Atendimento.Producao;
using EasyStock.Application.UseCases.Inventario.Desacertos;
using EasyStock.Domain.Entities.Storefront;
using EasyStock.Domain.ValueObjects;
using Microsoft.Extensions.Time.Testing;
using StorefrontEntity = EasyStock.Domain.Entities.Storefront.Storefront;

namespace EasyStock.Application.Tests.UseCases.Atendimento.Producao;

/// <summary>
/// M2.1 (#1490): o estoque do dia por prato do cardápio, em porções, com os lotes e o vencimento
/// no dia operacional do Brasil, e os alertas de venda sem saldo ligados ao prato.
/// </summary>
public class EstoqueDoDiaUseCaseTests
{
    private static readonly Guid EmpresaId = Guid.NewGuid();
    // 09/10/2026 12:00 em Brasília (15:00 UTC).
    private readonly FakeTimeProvider _relogio = new(new DateTimeOffset(2026, 10, 9, 15, 0, 0, TimeSpan.Zero));
    private readonly StorefrontEntity _vitrine = StorefrontEntity.Criar(EmpresaId, "casa-da-baba", "Casa da Baba", 0m);
    private readonly IStorefrontRepository _storefronts = Substitute.For<IStorefrontRepository>();
    private readonly ICardapioItemRepository _cardapio = Substitute.For<ICardapioItemRepository>();
    private readonly IItemEstoqueRepository _itens = Substitute.For<IItemEstoqueRepository>();
    private readonly IDesacertosEstoqueQueries _desacertos = Substitute.For<IDesacertosEstoqueQueries>();

    public EstoqueDoDiaUseCaseTests()
    {
        _vitrine.Ativar();
        _storefronts.GetByEmpresaAsync(EmpresaId, Arg.Any<CancellationToken>()).Returns(_vitrine);
        _desacertos.ListarAsync(EmpresaId, null, Arg.Any<CancellationToken>()).Returns([]);
    }

    private EstoqueDoDiaUseCase Sut() => new(_storefronts, _cardapio, _itens, new ListarDesacertosEstoqueUseCase(_desacertos), _relogio);

    private CardapioItem Prato(Guid produtoId, string nome)
    {
        var p = (Produto)Activator.CreateInstance(typeof(Produto), nonPublic: true)!;
        typeof(Produto).GetProperty("Id")!.SetValue(p, produtoId);
        var item = CardapioItem.CriarAPartirDeProduto(_vitrine.Id, p);
        item.AtualizarMetadata(nomePublico: nome);
        return item;
    }

    private static ItemEstoque Lote(Guid produtoId, decimal atual, DateTime? validade, decimal descoberto = 0m, string codigo = "LOT-1") => new()
    {
        Id = Guid.NewGuid(), EmpresaId = EmpresaId, ProdutoId = produtoId,
        QuantidadeInicial = Quantidade.From(10), QuantidadeAtual = Quantidade.From(atual),
        QuantidadeDescoberta = Quantidade.From(descoberto), CodigoLote = CodigoLote.From(codigo),
        ValidadeEm = validade is null ? null : Validade.From(validade.Value),
        CustoUnitario = Dinheiro.FromDecimal(4m), Status = StatusItemEstoque.Ok,
        EntradaEm = DateTime.UtcNow.AddDays(-1), CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow,
    };

    [Fact]
    public async Task SaldoEmPorcoes_SoDeLoteNaoVencido_EVencendoDestacado()
    {
        var lasanhaId = Guid.NewGuid();
        var lasanha = Prato(lasanhaId, "Lasanha");
        var avulso = CardapioItem.CriarAvulso(_vitrine.Id, "Bolo do dia", 20m);
        _cardapio.GetTodosDoStorefrontAsync(_vitrine.Id, Arg.Any<CancellationToken>()).Returns([lasanha, avulso]);
        _itens.GetByProdutosAsync(EmpresaId, Arg.Any<IEnumerable<Guid>>(), null, Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, IReadOnlyCollection<ItemEstoque>>
            {
                [lasanhaId] =
                [
                    Lote(lasanhaId, 3, new DateTime(2026, 10, 10), codigo: "LOT-AMANHA"),
                    Lote(lasanhaId, 5, new DateTime(2026, 10, 20), codigo: "LOT-LONGE"),
                    Lote(lasanhaId, 2, new DateTime(2026, 10, 8), codigo: "LOT-VENCIDO"),
                    Lote(lasanhaId, 0, new DateTime(2026, 10, 30), descoberto: 1, codigo: "LOT-ZERADO"),
                ],
            });

        var r = await Sut().ExecuteAsync(EmpresaId);

        var prato = r.Pratos.Should().ContainSingle("o avulso não tem saldo").Subject;
        prato.Saldo.Should().Be(8, "3 + 5; o lote vencido não conta");
        prato.Descoberto.Should().Be(1);
        prato.Lotes.Select(l => (l.Codigo, l.Vencendo, l.Vencido)).Should().Equal(
            ("LOT-VENCIDO", false, true), ("LOT-AMANHA", true, false), ("LOT-LONGE", false, false));
    }

    [Fact]
    public async Task AlertaDeVendaSemSaldo_VemEmTexto_ELigadoAoPrato()
    {
        var lasanhaId = Guid.NewGuid();
        var lasanha = Prato(lasanhaId, "Lasanha");
        _cardapio.GetTodosDoStorefrontAsync(_vitrine.Id, Arg.Any<CancellationToken>()).Returns([lasanha]);
        _itens.GetByProdutosAsync(EmpresaId, Arg.Any<IEnumerable<Guid>>(), null, Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, IReadOnlyCollection<ItemEstoque>>());
        _desacertos.ListarAsync(EmpresaId, null, Arg.Any<CancellationToken>()).Returns(
        [
            new DesacertoProdutoLinha(lasanhaId, "Lasanha", 2, 0, [new SaidaDesacertoLinha(DateTime.UtcNow, 2, "pedido:K7")]),
            new DesacertoProdutoLinha(Guid.NewGuid(), "Insumo", 1, 0, []),
        ]);

        var r = await Sut().ExecuteAsync(EmpresaId);

        r.Alertas.Should().HaveCount(2);
        r.Alertas[0].CardapioItemId.Should().Be(lasanha.Id);
        r.Alertas[0].Texto.Should().NotBeNullOrWhiteSpace();
        r.Alertas[1].CardapioItemId.Should().BeNull("produto fora do cardápio");
    }
}
