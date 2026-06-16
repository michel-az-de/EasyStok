using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.Ports.Output.Storage;
using EasyStock.Application.UseCases.Admin.Storefront.Cardapio.RemoverCardapioItemAdmin;
using EasyStock.Domain.Entities.Storefront;
using EasyStock.Domain.Exceptions.Storefront;

namespace EasyStock.Application.Tests.UseCases.Admin.Storefront.Cardapio;

public class RemoverCardapioItemAdminUseCaseTests
{
    private readonly ICardapioItemRepository _repo = Substitute.For<ICardapioItemRepository>();
    private readonly IFileStorage _storage = Substitute.For<IFileStorage>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    private RemoverCardapioItemAdminUseCase Sut() => new(_repo, _storage, _uow);

    [Fact]
    public async Task DeveRemoverEComitar_QuandoItemExiste()
    {
        var storefrontId = Guid.NewGuid();
        var item = CardapioItem.CriarAvulso(storefrontId, "Lasanha", 35m);
        _repo.GetByIdAndScopeAsync(storefrontId, item.Id, Arg.Any<Guid?>(), Arg.Any<CancellationToken>()).Returns(item);

        var result = await Sut().ExecuteAsync(new RemoverCardapioItemAdminCommand(storefrontId, item.Id));

        result.ItemId.Should().Be(item.Id);
        await _repo.Received(1).RemoveAsync(item, Arg.Any<CancellationToken>());
        await _uow.Received(1).CommitAsync();
    }

    [Fact]
    public async Task DeveLancar404_ENaoRemover_QuandoItemDeOutraEmpresa()
    {
        var storefrontId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var empresaIntrusa = Guid.NewGuid();
        _repo.GetByIdAndScopeAsync(storefrontId, itemId, empresaIntrusa, Arg.Any<CancellationToken>())
            .Returns((CardapioItem?)null);

        var act = async () => await Sut().ExecuteAsync(new RemoverCardapioItemAdminCommand(storefrontId, itemId, empresaIntrusa));

        await act.Should().ThrowAsync<CardapioItemNaoEncontradoException>();
        await _repo.DidNotReceive().RemoveAsync(Arg.Any<CardapioItem>(), Arg.Any<CancellationToken>());
    }
}
