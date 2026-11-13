using EasyStock.Domain.Entities.Storefront;
using FluentAssertions;

namespace EasyStock.Domain.Tests.Entities.Storefront;

/// <summary>
/// #2018 (comanda-09): "o item pode ser vendido" tinha quatro cópias (listagem, checkout, agente e aviso de interesse) com
/// critérios diferentes. É <see cref="CardapioItem.EstaOfertado"/>: visível, não arquivado, disponível, com ao menos uma
/// porção à venda e fora de categoria escondida.
/// </summary>
public class CardapioItemOfertadoTests
{
    private static CardapioItem Item(bool visivel = true)
    {
        var item = CardapioItem.CriarAvulso(Guid.NewGuid(), "Ravioli de Abóbora", 35m, categoria: "massas");
        if (visivel) item.TornarVisivel();
        return item;
    }

    [Fact]
    public void ItemVisivelEDisponivel_EOfertado() => Item().EstaOfertado().Should().BeTrue();

    [Fact]
    public void ItemOculto_NaoEOfertado() => Item(visivel: false).EstaOfertado().Should().BeFalse();

    [Fact]
    public void ItemEsgotado_NaoEOfertado()
    {
        var item = Item();
        item.MarcarEsgotado();

        item.EstaOfertado().Should().BeFalse();
    }

    [Fact]
    public void ItemArquivado_NaoEOfertado()
    {
        var item = Item();
        item.Arquivar(DateTime.UtcNow);

        item.EstaOfertado().Should().BeFalse();
    }

    [Fact]
    public void ItemEmCategoriaEscondida_NaoEOfertado()
    {
        var item = Item();
        item.Secao = CardapioSecao.CriarRaiz(item.StorefrontId, "Massas");
        item.EstaOfertado().Should().BeTrue();

        item.Secao.AlterarVisibilidade(false);

        item.EstaOfertado().Should().BeFalse();
    }

    [Fact]
    public void ItemSemSecaoCarregada_NaoEBarradoPelaCategoria() => Item().EmSecaoOculta.Should().BeFalse();

    [Fact]
    public void ItemComTodasAsPorcoesEsgotadas_NaoEOfertado()
    {
        var item = Item();
        var p300 = CardapioItemVariacao.Criar(item.Id, "300g", 28m, ordemExibicao: 1);
        var p800 = CardapioItemVariacao.Criar(item.Id, "800g", 42m, ordemExibicao: 2);
        item.AdicionarVariacao(p300);
        item.AdicionarVariacao(p800);
        p300.MarcarEsgotado();
        item.EstaOfertado().Should().BeTrue("ainda há a porção de 800g");

        p800.MarcarEsgotado();

        item.EstaOfertado().Should().BeFalse();
    }
}
