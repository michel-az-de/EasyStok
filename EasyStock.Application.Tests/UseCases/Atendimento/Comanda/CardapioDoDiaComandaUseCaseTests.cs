using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Campanhas;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.Services.Campanhas;
using EasyStock.Application.UseCases.Admin.Storefront.Cardapio.ToggleDisponibilidadeCardapioItemAdmin;
using EasyStock.Application.UseCases.Atendimento.Comanda;
using EasyStock.Application.UseCases.Inventario.Desacertos;
using EasyStock.Domain.Entities.Storefront;
using EasyStock.Domain.Exceptions.Storefront;
using StorefrontEntity = EasyStock.Domain.Entities.Storefront.Storefront;

namespace EasyStock.Application.Tests.UseCases.Atendimento.Comanda;

/// <summary>
/// #1241 (F11): o operador liga/desliga o item hoje e ajusta o saldo pelo cardapio_item_id. O produto
/// do saldo é resolvido na vitrine da empresa logada; item de outra empresa é 404.
/// </summary>
public class CardapioDoDiaComandaUseCaseTests
{
    private static readonly Guid EmpresaId = Guid.NewGuid();
    private readonly StorefrontEntity _vitrine = StorefrontEntity.Criar(EmpresaId, "casa-da-baba", "Casa da Baba", 0m);
    private readonly IStorefrontRepository _storefronts = Substitute.For<IStorefrontRepository>();
    private readonly ICardapioItemRepository _cardapio = Substitute.For<ICardapioItemRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly IItemEstoqueRepository _itens = Substitute.For<IItemEstoqueRepository>();
    private readonly ICardapioPublicoCache _publico = Substitute.For<ICardapioPublicoCache>();

    public CardapioDoDiaComandaUseCaseTests()
    {
        _vitrine.Ativar();
        _storefronts.GetByEmpresaAsync(EmpresaId, Arg.Any<CancellationToken>()).Returns(_vitrine);
    }

    private CardapioDoDiaComandaUseCase Sut() => new(
        _storefronts,
        _cardapio,
        new ToggleDisponibilidadeCardapioItemAdminUseCase(_cardapio, _uow,
            new AvisoItemComInteresse(_storefronts, Substitute.For<IInteresseItemRepository>(), Substitute.For<IOperacaoEventPublisher>())),
        new AjustarSaldoRapidoUseCase(_itens, Substitute.For<IContagemRepository>(), Substitute.For<IMovimentacaoEstoqueRepository>(),
            _uow, Substitute.For<IOperacaoEventPublisher>(), TimeProvider.System),
        _publico);

    private CardapioItem ItemDeProduto(Guid produtoId, bool disponivel)
    {
        var p = (Produto)Activator.CreateInstance(typeof(Produto), nonPublic: true)!;
        typeof(Produto).GetProperty("Id")!.SetValue(p, produtoId);
        var item = CardapioItem.CriarAPartirDeProduto(_vitrine.Id, p);
        if (!disponivel) item.MarcarEsgotado();
        _cardapio.GetByIdAndScopeAsync(_vitrine.Id, item.Id, EmpresaId, Arg.Any<CancellationToken>()).Returns(item);
        return item;
    }

    [Fact]
    public async Task DefinirDisponibilidade_MudaQuandoDiferente()
    {
        var item = ItemDeProduto(Guid.NewGuid(), disponivel: true);

        var r = await Sut().DefinirDisponibilidadeAsync(new DefinirDisponibilidadeItemInput(EmpresaId, item.Id, false));

        r.Disponivel.Should().BeFalse();
        item.Disponivel.Should().BeFalse();
        await _uow.Received(1).CommitAsync();
    }

    // #2002 comanda-01: esgotar o prato tem de chegar ao menu público na hora, não depois do prazo do cache.
    [Fact]
    public async Task DefinirDisponibilidade_InvalidaOMenuPublico_SoQuandoMuda()
    {
        var item = ItemDeProduto(Guid.NewGuid(), disponivel: true);

        await Sut().DefinirDisponibilidadeAsync(new DefinirDisponibilidadeItemInput(EmpresaId, item.Id, false));
        await Sut().DefinirDisponibilidadeAsync(new DefinirDisponibilidadeItemInput(EmpresaId, item.Id, false));

        _publico.Received(1).Invalidar("casa-da-baba");
    }

    // #2002 comanda-08: duas atendentes esgotam o mesmo prato juntas. As duas viram "disponível"; o toggle relia o prato
    // (já esgotado pela primeira) e invertia de volta. Pedir o valor, em vez de inverter, deixa o prato esgotado.
    [Fact]
    public async Task DefinirDisponibilidade_DuasAtendentesEsgotandoJuntas_NaoReligamOPrato()
    {
        var visto = ItemDeProduto(Guid.NewGuid(), disponivel: true);
        var relido = CardapioItem.CriarAvulso(_vitrine.Id, "Lasanha", 20m);
        relido.MarcarEsgotado();
        _cardapio.GetByIdAndScopeAsync(_vitrine.Id, visto.Id, EmpresaId, Arg.Any<CancellationToken>()).Returns(visto, relido);

        var r = await Sut().DefinirDisponibilidadeAsync(new DefinirDisponibilidadeItemInput(EmpresaId, visto.Id, false));

        r.Disponivel.Should().BeFalse();
        relido.Disponivel.Should().BeFalse("a segunda atendente pediu esgotado: o prato que a primeira esgotou não volta a ser vendido");
    }

    [Fact]
    public async Task DefinirDisponibilidade_MesmoValorNaoInverte()
    {
        var item = ItemDeProduto(Guid.NewGuid(), disponivel: false);

        var r = await Sut().DefinirDisponibilidadeAsync(new DefinirDisponibilidadeItemInput(EmpresaId, item.Id, false));

        r.Disponivel.Should().BeFalse("o segundo clique repetido não pode religar o item");
        await _uow.DidNotReceive().CommitAsync();
    }

    [Fact]
    public async Task ItemDeOutraEmpresa_404()
    {
        var act = () => Sut().DefinirDisponibilidadeAsync(new DefinirDisponibilidadeItemInput(EmpresaId, Guid.NewGuid(), true));

        await act.Should().ThrowAsync<CardapioItemNaoEncontradoException>();
    }

    [Fact]
    public async Task AjustarSaldo_ResolveOProdutoDoItem()
    {
        var produtoId = Guid.NewGuid();
        var item = ItemDeProduto(produtoId, disponivel: true);
        _itens.GetLotesParaAjusteAsync(EmpresaId, produtoId, null, Arg.Any<CancellationToken>()).Returns([]);

        var act = () => Sut().AjustarSaldoAsync(new AjustarSaldoItemInput(EmpresaId, Guid.NewGuid(), item.Id, null, 4m, "contei"));

        // Sem lote, o ajuste rápido recusa; o importante é que consultou os lotes DESTE produto.
        await act.Should().ThrowAsync<UseCaseValidationException>().WithMessage("*registre uma entrada*");
        await _itens.Received(1).GetLotesParaAjusteAsync(EmpresaId, produtoId, null, Arg.Any<CancellationToken>());
    }

    // ── #1542 (M1.4d): a contagem de prato com porções é de uma porção ─────────────────────────

    private (CardapioItem Item, Guid ProdutoId, CardapioItemVariacao P300, ItemEstoque Lote300, ItemEstoque Lote800) RavioliComPorcoes()
    {
        var produtoId = Guid.NewGuid();
        var item = ItemDeProduto(produtoId, disponivel: true);
        var p300 = CardapioItemVariacao.Criar(item.Id, "300 g", 28m, ordemExibicao: 0, ehPadrao: true, produtoVariacaoId: Guid.NewGuid());
        var p800 = CardapioItemVariacao.Criar(item.Id, "800 g", 62m, ordemExibicao: 1, produtoVariacaoId: Guid.NewGuid());
        item.AdicionarVariacao(p300);
        item.AdicionarVariacao(p800);
        ItemEstoque Lote(Guid? porcao, decimal atual, int diasAtras) => new()
        {
            Id = Guid.NewGuid(), EmpresaId = EmpresaId, ProdutoId = produtoId, ProdutoVariacaoId = porcao,
            QuantidadeInicial = EasyStock.Domain.ValueObjects.Quantidade.From(10),
            QuantidadeAtual = EasyStock.Domain.ValueObjects.Quantidade.From(atual),
            CustoUnitario = EasyStock.Domain.ValueObjects.Dinheiro.FromDecimal(4m), Status = StatusItemEstoque.Ok,
            EntradaEm = DateTime.UtcNow.AddDays(-diasAtras), CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow,
        };
        var lote300 = Lote(p300.ProdutoVariacaoId, 4m, 5);
        var lote800 = Lote(p800.ProdutoVariacaoId, 3m, 1);
        _itens.GetLotesParaAjusteAsync(EmpresaId, produtoId, null, Arg.Any<CancellationToken>()).Returns([lote300, lote800]);
        return (item, produtoId, p300, lote300, lote800);
    }

    [Fact]
    public async Task AjustarSaldo_DaPorcao_ContaSoOsLotesDela()
    {
        var (item, _, p300, lote300, lote800) = RavioliComPorcoes();

        await Sut().AjustarSaldoAsync(new AjustarSaldoItemInput(EmpresaId, Guid.NewGuid(), item.Id, null, 6m, "contei", VariacaoId: p300.Id));

        lote300.QuantidadeAtual.Value.Should().Be(6m);
        lote800.QuantidadeAtual.Value.Should().Be(3m);
    }

    [Fact]
    public async Task AjustarSaldo_PorcaoQueNaoEDoPrato_Recusa()
    {
        var (item, _, _, lote300, _) = RavioliComPorcoes();

        var act = () => Sut().AjustarSaldoAsync(new AjustarSaldoItemInput(EmpresaId, Guid.NewGuid(), item.Id, null, 6m, "contei", VariacaoId: Guid.NewGuid()));

        await act.Should().ThrowAsync<UseCaseValidationException>().WithMessage("*não tem essa porção*");
        lote300.QuantidadeAtual.Value.Should().Be(4m);
    }

    [Fact]
    public async Task AjustarSaldo_PorcaoAindaSemEstoque_PedeProducao()
    {
        var item = ItemDeProduto(Guid.NewGuid(), disponivel: true);
        var semLote = CardapioItemVariacao.Criar(item.Id, "300 g", 28m, ordemExibicao: 0, ehPadrao: true);
        item.AdicionarVariacao(semLote);

        var act = () => Sut().AjustarSaldoAsync(new AjustarSaldoItemInput(EmpresaId, Guid.NewGuid(), item.Id, null, 6m, "contei", VariacaoId: semLote.Id));

        await act.Should().ThrowAsync<UseCaseValidationException>().WithMessage("*produção*");
    }

    [Fact]
    public async Task AjustarSaldo_PratoComPorcoesSemPorcaoInformada_SegueContandoOPratoInteiro()
    {
        // Aba antiga do console ainda manda a contagem do prato: a rota não pode passar a recusar.
        var (item, _, _, lote300, lote800) = RavioliComPorcoes();

        await Sut().AjustarSaldoAsync(new AjustarSaldoItemInput(EmpresaId, Guid.NewGuid(), item.Id, null, 9m, "contei"));

        (lote300.QuantidadeAtual.Value + lote800.QuantidadeAtual.Value).Should().Be(9m);
    }

    // #2002: o menu público mostra o saldo (estoqueAtual); a contagem nova não espera o prazo do cache.
    [Fact]
    public async Task AjustarSaldo_InvalidaOMenuPublico()
    {
        var (item, _, _, _, _) = RavioliComPorcoes();

        await Sut().AjustarSaldoAsync(new AjustarSaldoItemInput(EmpresaId, Guid.NewGuid(), item.Id, null, 9m, "contei"));

        _publico.Received(1).Invalidar("casa-da-baba");
    }

    [Fact]
    public async Task AjustarSaldo_ItemAvulsoNaoControlaSaldo()
    {
        var avulso = CardapioItem.CriarAvulso(_vitrine.Id, "Bolo do dia", 20m, "Doces");
        _cardapio.GetByIdAndScopeAsync(_vitrine.Id, avulso.Id, EmpresaId, Arg.Any<CancellationToken>()).Returns(avulso);

        var act = () => Sut().AjustarSaldoAsync(new AjustarSaldoItemInput(EmpresaId, Guid.NewGuid(), avulso.Id, null, 4m, "contei"));

        await act.Should().ThrowAsync<UseCaseValidationException>().WithMessage("*não controla saldo*");
    }
}
