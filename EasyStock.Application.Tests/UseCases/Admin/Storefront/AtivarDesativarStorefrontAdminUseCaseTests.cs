using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.UseCases.Admin.Storefront.AtivarStorefrontAdmin;
using EasyStock.Application.UseCases.Admin.Storefront.DesativarStorefrontAdmin;
using EasyStock.Domain.Exceptions.Storefront;
using StorefrontEntity = EasyStock.Domain.Entities.Storefront.Storefront;

namespace EasyStock.Application.Tests.UseCases.Admin.Storefront;

public class AtivarDesativarStorefrontAdminUseCaseTests
{
    private readonly IStorefrontRepository _repo = Substitute.For<IStorefrontRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    private AtivarStorefrontAdminUseCase Ativar() => new(_repo, _uow);
    private DesativarStorefrontAdminUseCase Desativar() => new(_repo, _uow);

    [Fact]
    public async Task Ativar_DeveLigarFlag_QuandoExiste()
    {
        var s = StorefrontEntity.Criar(Guid.NewGuid(), "slug-x", "Loja X", 0m);
        // Storefront é criado inativo (default safe) — Ativar liga.
        _repo.GetByIdAsync(s.Id, Arg.Any<CancellationToken>()).Returns(s);

        var result = await Ativar().ExecuteAsync(new AtivarStorefrontAdminCommand(s.Id));

        result.Ativo.Should().BeTrue();
        s.Ativo.Should().BeTrue();
        await _repo.Received(1).UpdateAsync(s, Arg.Any<CancellationToken>());
        await _uow.Received(1).CommitAsync();
    }

    [Fact]
    public async Task Ativar_DeveSerIdempotente_QuandoJaAtivo()
    {
        var s = StorefrontEntity.Criar(Guid.NewGuid(), "slug-y", "Loja Y", 0m);
        s.Ativar();
        _repo.GetByIdAsync(s.Id, Arg.Any<CancellationToken>()).Returns(s);

        var result = await Ativar().ExecuteAsync(new AtivarStorefrontAdminCommand(s.Id));

        result.Ativo.Should().BeTrue();
        // Repo é chamado mesmo quando idempotente — semantic de "save unconditionally".
    }

    [Fact]
    public async Task Ativar_DeveLancar404_QuandoNaoExiste()
    {
        var id = Guid.NewGuid();
        _repo.GetByIdAsync(id, Arg.Any<CancellationToken>())
            .Returns((StorefrontEntity?)null);

        var act = async () => await Ativar().ExecuteAsync(new AtivarStorefrontAdminCommand(id));
        await act.Should().ThrowAsync<StorefrontNaoEncontradoException>();
    }

    [Fact]
    public async Task Desativar_DeveDesligarFlag_QuandoAtivo()
    {
        var s = StorefrontEntity.Criar(Guid.NewGuid(), "slug-z", "Loja Z", 0m);
        s.Ativar();
        _repo.GetByIdAsync(s.Id, Arg.Any<CancellationToken>()).Returns(s);

        var result = await Desativar().ExecuteAsync(new DesativarStorefrontAdminCommand(s.Id));

        result.Ativo.Should().BeFalse();
        s.Ativo.Should().BeFalse();
    }

    [Fact]
    public async Task Desativar_DeveLancar404_QuandoNaoExiste()
    {
        var id = Guid.NewGuid();
        _repo.GetByIdAsync(id, Arg.Any<CancellationToken>())
            .Returns((StorefrontEntity?)null);

        var act = async () => await Desativar().ExecuteAsync(new DesativarStorefrontAdminCommand(id));
        await act.Should().ThrowAsync<StorefrontNaoEncontradoException>();
    }
}
