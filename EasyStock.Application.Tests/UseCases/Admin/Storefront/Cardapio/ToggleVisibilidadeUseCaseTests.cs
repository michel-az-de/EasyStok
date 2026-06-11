using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.UseCases.Admin.Storefront.Cardapio.ToggleVisibilidadeCardapioItemAdmin;
using EasyStock.Domain.Entities.Storefront;
using EasyStock.Domain.Exceptions.Storefront;

namespace EasyStock.Application.Tests.UseCases.Admin.Storefront.Cardapio;

public class ToggleVisibilidadeUseCaseTests
{
    private readonly ICardapioItemRepository _repo = Substitute.For<ICardapioItemRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    private ToggleVisibilidadeCardapioItemAdminUseCase Sut() => new(_repo, _uow);

    private static CardapioItem ItemFake(Guid storefrontId, bool visivel = false)
    {
        // CriarAPartirDeProduto exige Produto válido; reflection é mais cheap para teste.
        var p = (Produto)System.Activator.CreateInstance(typeof(Produto), nonPublic: true)!;
        typeof(Produto).GetProperty("Id")!.SetValue(p, Guid.NewGuid());
        var item = CardapioItem.CriarAPartirDeProduto(storefrontId, p);
        if (visivel) item.TornarVisivel();
        return item;
    }

    [Fact]
    public async Task DeveTornarVisivel_QuandoOculto()
    {
        var storefrontId = Guid.NewGuid();
        var item = ItemFake(storefrontId, visivel: false);
        _repo.GetByIdAndScopeAsync(storefrontId, item.Id, Arg.Any<Guid?>(), Arg.Any<CancellationToken>()).Returns(item);

        var result = await Sut().ExecuteAsync(
            new ToggleVisibilidadeCardapioItemAdminCommand(storefrontId, item.Id));

        result.VisivelAgora.Should().BeTrue();
        item.Visivel.Should().BeTrue();
        await _uow.Received(1).CommitAsync();
    }

    [Fact]
    public async Task DeveOcultar_QuandoVisivel()
    {
        var storefrontId = Guid.NewGuid();
        var item = ItemFake(storefrontId, visivel: true);
        _repo.GetByIdAndScopeAsync(storefrontId, item.Id, Arg.Any<Guid?>(), Arg.Any<CancellationToken>()).Returns(item);

        var result = await Sut().ExecuteAsync(
            new ToggleVisibilidadeCardapioItemAdminCommand(storefrontId, item.Id));

        result.VisivelAgora.Should().BeFalse();
        item.Visivel.Should().BeFalse();
    }

    [Fact]
    public async Task DeveLancar404_QuandoItemNaoExiste()
    {
        var storefrontId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        _repo.GetByIdAndScopeAsync(storefrontId, itemId, Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns((CardapioItem?)null);

        var act = async () => await Sut().ExecuteAsync(
            new ToggleVisibilidadeCardapioItemAdminCommand(storefrontId, itemId));

        await act.Should().ThrowAsync<CardapioItemNaoEncontradoException>();
    }
}
