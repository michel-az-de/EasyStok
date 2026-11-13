using EasyStock.Application.Services.Storefront;
using EasyStock.Domain.Entities.Storefront;

namespace EasyStock.Application.Tests.Services.Storefront;

/// <summary>
/// #2002 comanda-02 e comanda-03: o que o checkout considera "vendável". Categoria escondida esconde os pratos dela da
/// venda, não só da listagem; e o prato que o pedido já reservou não trava a alteração quando esgota.
/// </summary>
public class CheckoutPratoVendavelTests
{
    private static CardapioSecao SecaoOculta(CheckoutCoreServiceTests.Cenario c)
    {
        var secao = CardapioSecao.CriarRaiz(c.Storefront.Id, "Páscoa");
        secao.AlterarVisibilidade(false);
        c.CardapioItem.DefinirSecao(secao.Id);
        c.CardapioItem.Secao = secao;
        return secao;
    }

    [Fact]
    public async Task PratoDeCategoriaOculta_NaoSeVende_AindaQueEstejaNoCarrinho()
    {
        var c = new CheckoutCoreServiceTests.Cenario();
        SecaoOculta(c);

        var agir = () => c.Servico().CarregarItensCardapioAsync(c.Storefront.Id, [c.CardapioItemId]);

        await agir.Should().ThrowAsync<RegraDeDominioVioladaException>().WithMessage("*indisponível*");
    }

    [Fact]
    public async Task PratoDeCategoriaVisivel_Vende()
    {
        var c = new CheckoutCoreServiceTests.Cenario();
        var secao = CardapioSecao.CriarRaiz(c.Storefront.Id, "Massas");
        c.CardapioItem.DefinirSecao(secao.Id);
        c.CardapioItem.Secao = secao;

        var itens = await c.Servico().CarregarItensCardapioAsync(c.Storefront.Id, [c.CardapioItemId]);

        itens.Should().ContainKey(c.CardapioItemId);
    }

    [Fact]
    public async Task PratoEsgotadoQueOPedidoJaTem_NaoTravaAAlteracao()
    {
        var c = new CheckoutCoreServiceTests.Cenario();
        c.CardapioItem.MarcarEsgotado();

        var itens = await c.Servico().CarregarItensCardapioAsync(c.Storefront.Id, [c.CardapioItemId],
            jaNoPedido: new HashSet<Guid> { c.CardapioItemId });

        itens.Should().ContainKey(c.CardapioItemId);
    }

    [Fact]
    public async Task PratoEsgotadoForaDoPedido_SegueRecusado()
    {
        var c = new CheckoutCoreServiceTests.Cenario();
        c.CardapioItem.MarcarEsgotado();

        var agir = () => c.Servico().CarregarItensCardapioAsync(c.Storefront.Id, [c.CardapioItemId],
            jaNoPedido: new HashSet<Guid> { Guid.NewGuid() });

        await agir.Should().ThrowAsync<RegraDeDominioVioladaException>().WithMessage("*indisponível*");
    }

    [Fact]
    public async Task PratoComTodasAsPorcoesEsgotadas_NaoSeVende_ComAMesmaMensagemDosDemais()
    {
        // #2018: a listagem já escondia o prato sem porção à venda; o checkout só descobria na hora de escolher a porção.
        var c = new CheckoutCoreServiceTests.Cenario();
        var porcao = CardapioItemVariacao.Criar(c.CardapioItem.Id, "300g", 28m);
        c.CardapioItem.AdicionarVariacao(porcao);
        porcao.MarcarEsgotado();

        var agir = () => c.Servico().CarregarItensCardapioAsync(c.Storefront.Id, [c.CardapioItemId]);

        await agir.Should().ThrowAsync<RegraDeDominioVioladaException>().WithMessage("*indisponível no cardápio*");
    }

    [Fact]
    public async Task PratoQueSaiuDoCardapio_NaoSegueNemQuandoOPedidoJaTem()
    {
        var c = new CheckoutCoreServiceTests.Cenario();
        var apagado = Guid.NewGuid();

        var agir = () => c.Servico().CarregarItensCardapioAsync(c.Storefront.Id, [apagado],
            jaNoPedido: new HashSet<Guid> { apagado });

        await agir.Should().ThrowAsync<RegraDeDominioVioladaException>().WithMessage("*não está mais no cardápio*");
    }

    [Fact]
    public async Task PorcaoEsgotadaQueOPedidoJaTem_MantemALinhaComOPrecoDela()
    {
        var c = new CheckoutCoreServiceTests.Cenario();
        var p300 = CardapioItemVariacao.Criar(c.CardapioItem.Id, "300 g", 28m, ordemExibicao: 0, ehPadrao: true);
        var p800 = CardapioItemVariacao.Criar(c.CardapioItem.Id, "800 g", 62m, ordemExibicao: 1);
        c.CardapioItem.AdicionarVariacao(p300);
        c.CardapioItem.AdicionarVariacao(p800);
        p800.MarcarEsgotado();
        var pedido = new EasyStock.Domain.Entities.Pedido { Id = Guid.NewGuid(), EmpresaId = c.Storefront.EmpresaId };
        var menu = new Dictionary<Guid, CardapioItem> { [c.CardapioItemId] = c.CardapioItem };
        var linha = new ItemPedidoCheckout(c.CardapioItemId, 1, null, p800.Id);

        var semMantida = () => c.Servico().AdicionarItensAsync(pedido, [linha], menu);
        await semMantida.Should().ThrowAsync<RegraDeDominioVioladaException>().WithMessage("*800 g*esgotad*");

        var criados = await c.Servico().AdicionarItensAsync(pedido, [linha], menu,
            mantidas: new HashSet<(Guid, Guid?)> { (c.CardapioItemId, p800.Id) });

        var item = criados.Should().ContainSingle().Subject;
        (item.CardapioItemVariacaoId, item.PrecoUnitario, item.VariacaoRotuloSnapshot).Should().Be((p800.Id, 62m, "800 g"));
    }
}
