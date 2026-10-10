using System.Text.Json;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.UseCases.Atendimento.Comanda;
using EasyStock.Application.UseCases.Storefront.Menu;
using EasyStock.Domain.Entities.Storefront;
using Microsoft.Extensions.Logging;
using StorefrontEntity = EasyStock.Domain.Entities.Storefront.Storefront;

namespace EasyStock.Application.Tests.UseCases.Atendimento.Comanda;

/// <summary>
/// M1.4b (#1531): o cardápio da comanda traz as porções à parte, por item, e o DTO do menu público
/// segue sem elas (ADR-0035, Fase 2 só depois do site).
/// </summary>
public class ListarCardapioComandaUseCaseTests
{
    private readonly StorefrontEntity _vitrine = StorefrontEntity.Criar(Guid.NewGuid(), "casa-da-baba", "Casa da Baba", 0m);
    private readonly IStorefrontRepository _storefronts = Substitute.For<IStorefrontRepository>();
    private readonly ICardapioItemRepository _cardapio = Substitute.For<ICardapioItemRepository>();
    private readonly List<CardapioItem> _itens = [];

    public ListarCardapioComandaUseCaseTests()
    {
        _vitrine.Ativar();
        _storefronts.GetByEmpresaAsync(_vitrine.EmpresaId, Arg.Any<CancellationToken>()).Returns(_vitrine);
        _storefronts.GetBySlugAsync(_vitrine.Slug, Arg.Any<CancellationToken>()).Returns(_vitrine);
        _cardapio.GetVisiveisDoStorefrontAsync(_vitrine.Id, Arg.Any<CancellationToken>()).Returns(_ => _itens.ToList());
    }

    private ListarCardapioComandaUseCase Sut() => new(_storefronts,
        new ListarCardapioPublicoUseCase(_storefronts, _cardapio, Substitute.For<IItemEstoqueRepository>(),
            Substitute.For<ILogger<ListarCardapioPublicoUseCase>>()), _cardapio);

    private CardapioItem Prato(string nome, decimal preco)
    {
        var item = CardapioItem.CriarAvulso(_vitrine.Id, nome, preco, "Massas");
        item.TornarVisivel();
        _itens.Add(item);
        return item;
    }

    [Fact]
    public async Task PorcoesVemPorItem_NaOrdem_ESoDeQuemTem()
    {
        var ravioli = Prato("Ravióli", 28m);
        ravioli.AdicionarVariacao(CardapioItemVariacao.Criar(ravioli.Id, "800 g", 62m, ordemExibicao: 1));
        var p300 = CardapioItemVariacao.Criar(ravioli.Id, "300 g", 28m, ordemExibicao: 0, ehPadrao: true, pesoExibicao: "300 g");
        ravioli.AdicionarVariacao(p300);
        ravioli.Variacoes.Single(v => v.Rotulo == "800 g").MarcarEsgotado();
        var bolo = Prato("Bolo", 12m);

        var r = await Sut().ExecuteAsync(_vitrine.EmpresaId);

        r.Itens.Select(i => i.Id).Should().BeEquivalentTo([ravioli.Id, bolo.Id]);
        r.Porcoes.Keys.Should().Equal(ravioli.Id);
        r.Porcoes[ravioli.Id].Select(p => (p.Rotulo, p.Preco, p.Disponivel, p.Padrao)).Should().Equal(
            ("300 g", 28m, true, true), ("800 g", 62m, false, false));
    }

    [Fact]
    public async Task OItemDoMenuPublicoContinuaSemPorcoes()
    {
        var ravioli = Prato("Ravióli", 28m);
        ravioli.AdicionarVariacao(CardapioItemVariacao.Criar(ravioli.Id, "300 g", 28m));

        var r = await Sut().ExecuteAsync(_vitrine.EmpresaId);

        var json = JsonSerializer.Serialize(r.Itens[0], new JsonSerializerOptions(JsonSerializerDefaults.Web));
        json.Should().NotContain("opcoes").And.NotContain("porcoes", "o contrato público só muda na Fase 2 do ADR-0035");
    }
}
