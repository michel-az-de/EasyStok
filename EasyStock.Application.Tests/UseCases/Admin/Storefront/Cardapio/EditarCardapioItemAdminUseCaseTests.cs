using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.UseCases.Admin.Storefront.Cardapio.EditarCardapioItemAdmin;
using EasyStock.Domain.Entities.Storefront;

namespace EasyStock.Application.Tests.UseCases.Admin.Storefront.Cardapio;

/// <summary>
/// Trava o contrato de edição que alimenta o editor completo do Web:
/// <c>null</c> = "não tocar"; <c>""</c> = limpar. Sem isso, esvaziar um campo
/// opcional e salvar não limparia nada (o texto antigo voltaria).
/// </summary>
public class EditarCardapioItemAdminUseCaseTests
{
    private readonly ICardapioItemRepository _repo = Substitute.For<ICardapioItemRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    private EditarCardapioItemAdminUseCase Sut() => new(_repo, _uow);

    private CardapioItem ItemComDetalhes(Guid storefrontId)
    {
        var item = CardapioItem.CriarAvulso(storefrontId, "Lasanha", 35m, "Massas");
        item.AtualizarMetadata(descricaoPublica: "Massa fresca", ingredientes: "Ovos");
        _repo.GetByIdAndScopeAsync(storefrontId, item.Id, Arg.Any<Guid?>(), Arg.Any<CancellationToken>()).Returns(item);
        return item;
    }

    [Fact]
    public async Task Null_MantemOsCampos()
    {
        var storefrontId = Guid.NewGuid();
        var item = ItemComDetalhes(storefrontId);

        await Sut().ExecuteAsync(new EditarCardapioItemAdminCommand(
            storefrontId, item.Id,
            NomePublico: null, CategoriaTexto: null, DescricaoPublica: null,
            Ingredientes: null, Alergenos: null, SugestaoMolho: null, TempoPreparo: null,
            FotoUrl: null, PrecoStorefront: null, Tag: null, PesoExibicao: null, FiltrosJson: null));

        item.DescricaoPublica.Should().Be("Massa fresca");
        item.Ingredientes.Should().Be("Ovos");
    }

    [Fact]
    public async Task StringVazia_LimpaOsCampos()
    {
        var storefrontId = Guid.NewGuid();
        var item = ItemComDetalhes(storefrontId);

        await Sut().ExecuteAsync(new EditarCardapioItemAdminCommand(
            storefrontId, item.Id,
            NomePublico: null, CategoriaTexto: null, DescricaoPublica: "",
            Ingredientes: "", Alergenos: null, SugestaoMolho: null, TempoPreparo: null,
            FotoUrl: null, PrecoStorefront: null, Tag: null, PesoExibicao: null, FiltrosJson: null));

        item.DescricaoPublica.Should().BeEmpty();
        item.Ingredientes.Should().BeEmpty();
        await _uow.Received(1).CommitAsync();
    }
}
