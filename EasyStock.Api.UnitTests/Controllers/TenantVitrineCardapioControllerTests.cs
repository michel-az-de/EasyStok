using EasyStock.Api.Controllers.Storefront;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Campanhas;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.Services.Campanhas;
using EasyStock.Application.UseCases.Admin.Storefront.Cardapio.ToggleDisponibilidadeCardapioItemAdmin;
using EasyStock.Application.UseCases.Admin.Storefront.Cardapio.ToggleVisibilidadeCardapioItemAdmin;
using EasyStock.Domain.Entities.Storefront;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using StorefrontEntity = EasyStock.Domain.Entities.Storefront.Storefront;

namespace EasyStock.Api.UnitTests.Controllers;

/// <summary>
/// #2002: o PWA (e o BFF do Web) gravam o cardápio por esta rota. Esgotar ou esconder o prato tem de chegar ao menu
/// público na hora, como pelo console; antes só o prazo do cache da memória fazia isso.
/// </summary>
public class TenantVitrineCardapioControllerTests
{
    private readonly Guid _empresaId = Guid.NewGuid();
    private readonly StorefrontEntity _vitrine;
    private readonly ICardapioItemRepository _cardapio = Substitute.For<ICardapioItemRepository>();
    private readonly IStorefrontRepository _vitrines = Substitute.For<IStorefrontRepository>();
    private readonly ICardapioPublicoCache _publico = Substitute.For<ICardapioPublicoCache>();
    private readonly CardapioItem _item;

    public TenantVitrineCardapioControllerTests()
    {
        _vitrine = StorefrontEntity.Criar(_empresaId, "casa-da-baba", "Casa da Baba", 0m);
        _vitrines.GetByEmpresaAsync(_empresaId).Returns(_vitrine);
        _item = CardapioItem.CriarAvulso(_vitrine.Id, "Lasanha", 35m);
        _item.TornarVisivel();
        _cardapio.GetByIdAndScopeAsync(_vitrine.Id, _item.Id, _empresaId, Arg.Any<CancellationToken>()).Returns(_item);
    }

    private TenantVitrineCardapioController Controller()
    {
        var usuario = Substitute.For<ICurrentUserAccessor>();
        usuario.EmpresaId.Returns(_empresaId);
        var aviso = new AvisoItemComInteresse(_vitrines, Substitute.For<IInteresseItemRepository>(), Substitute.For<IOperacaoEventPublisher>());
        var uow = Substitute.For<IUnitOfWork>();
        return new TenantVitrineCardapioController(_vitrines, null!, null!, null!, null!, null!,
            new ToggleVisibilidadeCardapioItemAdminUseCase(_cardapio, uow, aviso),
            new ToggleDisponibilidadeCardapioItemAdminUseCase(_cardapio, uow, aviso),
            null!, null!, usuario, NullLogger<TenantVitrineCardapioController>.Instance, _publico);
    }

    [Fact]
    public async Task EsgotarPeloPwa_InvalidaOMenuPublicoDaVitrine()
    {
        await Controller().ToggleDisponivel(_item.Id);

        _item.Disponivel.Should().BeFalse();
        _publico.Received(1).Invalidar("casa-da-baba");
    }

    [Fact]
    public async Task EsconderPeloPwa_InvalidaOMenuPublicoDaVitrine()
    {
        await Controller().ToggleVisivel(_item.Id);

        _item.Visivel.Should().BeFalse();
        _publico.Received(1).Invalidar("casa-da-baba");
    }

    [Fact]
    public async Task ItemQueNaoExiste_NaoInvalida()
    {
        await Controller().ToggleDisponivel(Guid.NewGuid());

        _publico.DidNotReceiveWithAnyArgs().Invalidar(default!);
    }
}
