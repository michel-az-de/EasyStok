using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Campanhas;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.Services.Campanhas;
using EasyStock.Application.UseCases.Admin.Storefront.Cardapio.ToggleDisponibilidadeCardapioItemAdmin;
using EasyStock.Application.UseCases.Admin.Storefront.Cardapio.ToggleVisibilidadeCardapioItemAdmin;
using EasyStock.Domain.Entities.Storefront;

namespace EasyStock.Application.Tests.UseCases.Admin.Storefront.Cardapio;

/// <summary>
/// S31: quando o item volta a ser oferecido (visível e disponível) e há interesses abertos, o console
/// recebe <c>cardapio.item_com_interesse</c> depois do commit. Nada vai ao cliente (D8).
/// </summary>
public class ToggleDisponibilidadeTests
{
    private static readonly Guid EmpresaId = Guid.NewGuid();
    private static readonly Guid StorefrontId = Guid.NewGuid();

    private readonly ICardapioItemRepository _repo = Substitute.For<ICardapioItemRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly IStorefrontRepository _storefronts = Substitute.For<IStorefrontRepository>();
    private readonly IInteresseItemRepository _interesses = Substitute.For<IInteresseItemRepository>();
    private readonly IOperacaoEventPublisher _publisher = Substitute.For<IOperacaoEventPublisher>();

    private AvisoItemComInteresse Aviso() => new(_storefronts, _interesses, _publisher);

    private CardapioItem Item(bool visivel, bool disponivel)
    {
        var p = (Produto)Activator.CreateInstance(typeof(Produto), nonPublic: true)!;
        typeof(Produto).GetProperty("Id")!.SetValue(p, Guid.NewGuid());
        var item = CardapioItem.CriarAPartirDeProduto(StorefrontId, p);
        if (visivel) item.TornarVisivel();
        if (!disponivel) item.MarcarEsgotado();
        _repo.GetByIdAndScopeAsync(StorefrontId, item.Id, Arg.Any<Guid?>(), Arg.Any<CancellationToken>()).Returns(item);
        return item;
    }

    [Fact]
    public async Task PublicaInteresse()
    {
        var item = Item(visivel: true, disponivel: false);
        _interesses.ContarAbertosDoItemAsync(EmpresaId, item.Id, Arg.Any<CancellationToken>()).Returns(3);

        var result = await new ToggleDisponibilidadeCardapioItemAdminUseCase(_repo, _uow, Aviso())
            .ExecuteAsync(new ToggleDisponibilidadeCardapioItemAdminCommand(StorefrontId, item.Id, EmpresaId));

        result.DisponivelAgora.Should().BeTrue();
        Received.InOrder(() =>
        {
            _uow.CommitAsync();
            _publisher.PublicarAsync(EventosOperacao.CardapioItemComInteresse, EmpresaId,
                Arg.Is<CardapioItemComInteresseOperacao>(p => p.CardapioItemId == item.Id && p.Quantidade == 3),
                Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task SuperAdmin_ResolveEmpresaPeloStorefront()
    {
        var item = Item(visivel: true, disponivel: false);
        _storefronts.GetByIdAsync(StorefrontId, Arg.Any<CancellationToken>())
            .Returns(Domain.Entities.Storefront.Storefront.Criar(EmpresaId, "casa-da-baba", "Casa da Baba", 0m));
        _interesses.ContarAbertosDoItemAsync(EmpresaId, item.Id, Arg.Any<CancellationToken>()).Returns(1);

        await new ToggleDisponibilidadeCardapioItemAdminUseCase(_repo, _uow, Aviso())
            .ExecuteAsync(new ToggleDisponibilidadeCardapioItemAdminCommand(StorefrontId, item.Id, EmpresaId: null));

        await _publisher.Received(1).PublicarAsync(EventosOperacao.CardapioItemComInteresse, EmpresaId,
            Arg.Any<CardapioItemComInteresseOperacao>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SemInteresseAberto_NaoPublica()
    {
        var item = Item(visivel: true, disponivel: false);
        _interesses.ContarAbertosDoItemAsync(EmpresaId, item.Id, Arg.Any<CancellationToken>()).Returns(0);

        await new ToggleDisponibilidadeCardapioItemAdminUseCase(_repo, _uow, Aviso())
            .ExecuteAsync(new ToggleDisponibilidadeCardapioItemAdminCommand(StorefrontId, item.Id, EmpresaId));

        await _publisher.DidNotReceiveWithAnyArgs().PublicarAsync(default!, default, default!, default);
    }

    [Fact]
    public async Task MarcarEsgotado_OuDisponivelMasOculto_NaoPublica()
    {
        var disponivel = Item(visivel: true, disponivel: true);
        var ocultoEsgotado = Item(visivel: false, disponivel: false);
        _interesses.ContarAbertosDoItemAsync(EmpresaId, Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(3);
        var sut = new ToggleDisponibilidadeCardapioItemAdminUseCase(_repo, _uow, Aviso());

        await sut.ExecuteAsync(new ToggleDisponibilidadeCardapioItemAdminCommand(StorefrontId, disponivel.Id, EmpresaId));
        await sut.ExecuteAsync(new ToggleDisponibilidadeCardapioItemAdminCommand(StorefrontId, ocultoEsgotado.Id, EmpresaId));

        await _publisher.DidNotReceiveWithAnyArgs().PublicarAsync(default!, default, default!, default);
    }

    [Fact]
    public async Task TornarVisivel_ItemDisponivel_PublicaInteresse()
    {
        var item = Item(visivel: false, disponivel: true);
        _interesses.ContarAbertosDoItemAsync(EmpresaId, item.Id, Arg.Any<CancellationToken>()).Returns(2);

        await new ToggleVisibilidadeCardapioItemAdminUseCase(_repo, _uow, Aviso())
            .ExecuteAsync(new ToggleVisibilidadeCardapioItemAdminCommand(StorefrontId, item.Id, EmpresaId));

        await _publisher.Received(1).PublicarAsync(EventosOperacao.CardapioItemComInteresse, EmpresaId,
            Arg.Is<CardapioItemComInteresseOperacao>(p => p.Quantidade == 2), Arg.Any<CancellationToken>());
    }
}
