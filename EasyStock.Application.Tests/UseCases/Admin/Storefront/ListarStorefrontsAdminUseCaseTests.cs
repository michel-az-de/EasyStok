using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.UseCases.Admin.Storefront.ListarStorefrontsAdmin;
using StorefrontEntity = EasyStock.Domain.Entities.Storefront.Storefront;

namespace EasyStock.Application.Tests.UseCases.Admin.Storefront;

public class ListarStorefrontsAdminUseCaseTests
{
    private readonly IStorefrontRepository _storefrontRepo = Substitute.For<IStorefrontRepository>();
    private readonly ICardapioItemRepository _cardapioRepo = Substitute.For<ICardapioItemRepository>();

    private ListarStorefrontsAdminUseCase Sut() => new(_storefrontRepo, _cardapioRepo);

    [Fact]
    public async Task DeveListar_ComCounts_DoBatch_E_Default0_QuandoAusente()
    {
        var s1 = StorefrontEntity.Criar(Guid.NewGuid(), "slug-a", "A", 0m);
        var s2 = StorefrontEntity.Criar(Guid.NewGuid(), "slug-b", "B", 0m);
        _storefrontRepo.ListarAdminAsync(0, 20, null, null, Arg.Any<CancellationToken>())
            .Returns(((IReadOnlyList<StorefrontEntity>)new[] { s1, s2 }, 2));
        // s2 ausente do dicionário de propósito → deve cair no default 0 (GROUP BY omite zeros).
        _cardapioRepo.ContarPorStorefrontsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, int> { [s1.Id] = 3 });

        var result = await Sut().ExecuteAsync(new ListarStorefrontsAdminCommand(0, 20, null, null));

        result.Total.Should().Be(2);
        result.Itens.Should().HaveCount(2);
        result.Itens[0].CardapioCount.Should().Be(3);
        result.Itens[1].CardapioCount.Should().Be(0);
    }

    [Fact]
    public async Task DeveContarEmUmaQueryBatch_SemN1()
    {
        var s1 = StorefrontEntity.Criar(Guid.NewGuid(), "slug-a", "A", 0m);
        var s2 = StorefrontEntity.Criar(Guid.NewGuid(), "slug-b", "B", 0m);
        _storefrontRepo.ListarAdminAsync(0, 20, null, null, Arg.Any<CancellationToken>())
            .Returns(((IReadOnlyList<StorefrontEntity>)new[] { s1, s2 }, 2));
        _cardapioRepo.ContarPorStorefrontsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, int> { [s1.Id] = 1, [s2.Id] = 2 });

        await Sut().ExecuteAsync(new ListarStorefrontsAdminCommand(0, 20, null, null));

        // 1 query batch com todos os ids; nunca o COUNT por-item (que era o N+1).
        await _cardapioRepo.Received(1).ContarPorStorefrontsAsync(
            Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 2 && ids.Contains(s1.Id) && ids.Contains(s2.Id)),
            Arg.Any<CancellationToken>());
        await _cardapioRepo.DidNotReceive().ContarPorStorefrontAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
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
