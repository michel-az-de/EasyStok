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
            _uow, Substitute.For<IOperacaoEventPublisher>(), TimeProvider.System));

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

    [Fact]
    public async Task AjustarSaldo_ItemAvulsoNaoControlaSaldo()
    {
        var avulso = CardapioItem.CriarAvulso(_vitrine.Id, "Bolo do dia", 20m, "Doces");
        _cardapio.GetByIdAndScopeAsync(_vitrine.Id, avulso.Id, EmpresaId, Arg.Any<CancellationToken>()).Returns(avulso);

        var act = () => Sut().AjustarSaldoAsync(new AjustarSaldoItemInput(EmpresaId, Guid.NewGuid(), avulso.Id, null, 4m, "contei"));

        await act.Should().ThrowAsync<UseCaseValidationException>().WithMessage("*não controla saldo*");
    }
}
