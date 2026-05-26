using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.UseCases.Admin.Storefront.ListarStorefrontsAdmin;
using FluentAssertions;
using NSubstitute;
using StorefrontEntity = EasyStock.Domain.Entities.Storefront.Storefront;

namespace EasyStock.Application.Tests.UseCases.Admin.Storefront;

public class ListarStorefrontsAdminUseCaseTests
{
    private readonly IStorefrontRepository _storefrontRepo = Substitute.For<IStorefrontRepository>();
    private readonly ICardapioItemRepository _cardapioRepo = Substitute.For<ICardapioItemRepository>();

    private ListarStorefrontsAdminUseCase Sut() => new(_storefrontRepo, _cardapioRepo);

    [Fact]
    public async Task DeveListar_ComCounts()
    {
        var s1 = StorefrontEntity.Criar(Guid.NewGuid(), "slug-a", "A", 0m);
        var s2 = StorefrontEntity.Criar(Guid.NewGuid(), "slug-b", "B", 0m);
        _storefrontRepo.ListarAdminAsync(0, 20, null, null, Arg.Any<CancellationToken>())
            .Returns(((IReadOnlyList<StorefrontEntity>)new[] { s1, s2 }, 2));
        _cardapioRepo.ContarPorStorefrontAsync(s1.Id, Arg.Any<CancellationToken>()).Returns(3);
        _cardapioRepo.ContarPorStorefrontAsync(s2.Id, Arg.Any<CancellationToken>()).Returns(0);

        var result = await Sut().ExecuteAsync(new ListarStorefrontsAdminCommand(0, 20, null, null));

        result.Total.Should().Be(2);
        result.Itens.Should().HaveCount(2);
        result.Itens[0].CardapioCount.Should().Be(3);
        result.Itens[1].CardapioCount.Should().Be(0);
    }

    [Fact]
    public async Task DevePassarFiltros_AoRepo()
    {
        _storefrontRepo.ListarAdminAsync(40, 10, "casa", true, Arg.Any<CancellationToken>())
            .Returns(((IReadOnlyList<StorefrontEntity>)Array.Empty<StorefrontEntity>(), 0));

        var result = await Sut().ExecuteAsync(new ListarStorefrontsAdminCommand(40, 10, "casa", true));

        result.Total.Should().Be(0);
        await _storefrontRepo.Received(1).ListarAdminAsync(40, 10, "casa", true, Arg.Any<CancellationToken>());
    }
}
