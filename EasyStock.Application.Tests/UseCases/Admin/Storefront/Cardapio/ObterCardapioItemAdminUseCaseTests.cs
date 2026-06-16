using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.UseCases.Admin.Storefront.Cardapio.ObterCardapioItemAdmin;
using EasyStock.Domain.Entities.Storefront;
using EasyStock.Domain.Exceptions.Storefront;

namespace EasyStock.Application.Tests.UseCases.Admin.Storefront.Cardapio;

public class ObterCardapioItemAdminUseCaseTests
{
    private readonly ICardapioItemRepository _repo = Substitute.For<ICardapioItemRepository>();

    private ObterCardapioItemAdminUseCase Sut() => new(_repo);

    [Fact]
    public async Task DeveRetornarDetalheCompleto_QuandoItemExisteNoEscopo()
    {
        var storefrontId = Guid.NewGuid();
        var item = CardapioItem.CriarAvulso(storefrontId, "Lasanha", 35m, "Massas");
        item.AtualizarMetadata(descricaoPublica: "Massa fresca", ingredientes: "Ovos, farinha");
        _repo.GetByIdAndScopeAsync(storefrontId, item.Id, Arg.Any<Guid?>(), Arg.Any<CancellationToken>()).Returns(item);

        var result = await Sut().ExecuteAsync(new ObterCardapioItemAdminCommand(storefrontId, item.Id, Guid.NewGuid()));

        result.NomeEfetivo.Should().Be("lasanha");          // armazenado em lowercase
        result.PrecoEfetivo.Should().Be(35m);
        result.DescricaoPublica.Should().Be("Massa fresca"); // detalhes vêm no GET-by-id (não na listagem)
        result.Ingredientes.Should().Be("Ovos, farinha");
    }

    [Fact]
    public async Task DeveLancar404_QuandoItemDeOutraEmpresa()
    {
        // GetByIdAndScopeAsync devolve null quando o item não pertence à empresa do escopo (ADR-0031 §3).
        var storefrontId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var empresaIntrusa = Guid.NewGuid();
        _repo.GetByIdAndScopeAsync(storefrontId, itemId, empresaIntrusa, Arg.Any<CancellationToken>())
            .Returns((CardapioItem?)null);

        var act = async () => await Sut().ExecuteAsync(new ObterCardapioItemAdminCommand(storefrontId, itemId, empresaIntrusa));

        await act.Should().ThrowAsync<CardapioItemNaoEncontradoException>();
    }
}
