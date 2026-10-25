using EasyStock.Application.Ports.Output;
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

    private readonly IStorefrontRepository _vitrines = Substitute.For<IStorefrontRepository>();
    private readonly ICardapioPublicoCache _publico = Substitute.For<ICardapioPublicoCache>();

    private RemoverCardapioItemAdminUseCase Sut() => new(_repo, _storage, _uow, _vitrines, _publico);

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

    // #2002: excluir o prato tira ele do menu público na hora, não depois do prazo do cache.
    [Fact]
    public async Task DeveInvalidarOMenuPublicoDaVitrine_DepoisDoCommit()
    {
        var vitrine = EasyStock.Domain.Entities.Storefront.Storefront.Criar(Guid.NewGuid(), "casa-da-baba", "Casa da Baba", 0m);
        var item = CardapioItem.CriarAvulso(vitrine.Id, "Lasanha", 35m);
        _repo.GetByIdAndScopeAsync(vitrine.Id, item.Id, Arg.Any<Guid?>(), Arg.Any<CancellationToken>()).Returns(item);
        _vitrines.GetByIdAsync(vitrine.Id).Returns(vitrine);

        await Sut().ExecuteAsync(new RemoverCardapioItemAdminCommand(vitrine.Id, item.Id));

        _publico.Received(1).Invalidar("casa-da-baba");
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

    // #2027: a FotoUrl é texto gravado pelo tenant. Apontada para o arquivo de outra empresa, a remoção apagava o arquivo dela.
    [Fact]
    public async Task NaoApagaFotoDeOutraVitrine_QuandoFotoUrlApontaParaEla()
    {
        var storefrontId = Guid.NewGuid();
        var item = CardapioItem.CriarAvulso(storefrontId, "Lasanha", 35m);
        var alheia = $"cardapios/{Guid.NewGuid()}/{Guid.NewGuid()}/{Guid.NewGuid()}/foto.webp";
        item.AtualizarMetadata(fotoUrl: $"/files/{alheia}");
        _repo.GetByIdAndScopeAsync(storefrontId, item.Id, Arg.Any<Guid?>(), Arg.Any<CancellationToken>()).Returns(item);

        await Sut().ExecuteAsync(new RemoverCardapioItemAdminCommand(storefrontId, item.Id));

        await _storage.DidNotReceiveWithAnyArgs().DeleteAsync(default!, default);
    }

    [Fact]
    public async Task ApagaAFotoDoProprioPrato()
    {
        var storefrontId = Guid.NewGuid();
        var item = CardapioItem.CriarAvulso(storefrontId, "Lasanha", 35m);
        var propria = $"cardapios/{Guid.NewGuid()}/{storefrontId}/{item.Id}/foto.webp";
        item.AtualizarMetadata(fotoUrl: $"https://api.exemplo/files/{propria}");
        _repo.GetByIdAndScopeAsync(storefrontId, item.Id, Arg.Any<Guid?>(), Arg.Any<CancellationToken>()).Returns(item);

        await Sut().ExecuteAsync(new RemoverCardapioItemAdminCommand(storefrontId, item.Id));

        await _storage.Received(1).DeleteAsync(propria, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NaoApagaFotoDeOutroPratoDaMesmaVitrine()
    {
        // Prato duplicado aponta para a foto do original: remover a cópia não pode apagar a foto do original.
        var storefrontId = Guid.NewGuid();
        var item = CardapioItem.CriarAvulso(storefrontId, "Lasanha", 35m);
        item.AtualizarMetadata(fotoUrl: $"/files/cardapios/{Guid.NewGuid()}/{storefrontId}/{Guid.NewGuid()}/foto.webp");
        _repo.GetByIdAndScopeAsync(storefrontId, item.Id, Arg.Any<Guid?>(), Arg.Any<CancellationToken>()).Returns(item);

        await Sut().ExecuteAsync(new RemoverCardapioItemAdminCommand(storefrontId, item.Id));

        await _storage.DidNotReceiveWithAnyArgs().DeleteAsync(default!, default);
    }
}
