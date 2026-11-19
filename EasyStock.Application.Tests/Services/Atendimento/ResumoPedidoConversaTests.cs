using EasyStock.Application.Services.Atendimento;
using EasyStock.Application.Services.Storefront;
using EasyStock.Application.UseCases.Pedidos.Cobranca;
using EasyStock.Domain.Entities.Storefront;
using StorefrontEntity = EasyStock.Domain.Entities.Storefront.Storefront;

namespace EasyStock.Application.Tests.Services.Atendimento;

/// <summary>#1474 B6: o resumo ao cliente mostra o item como no cardápio e o horário da janela.</summary>
public class ResumoPedidoConversaTests
{
    private static readonly DateOnly SextaFeira = new(2026, 10, 9);

    private static PedidoReservado Reservado(string nomeItem, JanelaEntrega? janela)
    {
        var storefront = StorefrontEntity.Criar(Guid.NewGuid(), "casa-da-baba", "Casa da Baba", 0m);
        var pedido = Pedido.Criar(storefront.EmpresaId);
        var item = new PedidoItem { Id = Guid.NewGuid(), PedidoId = pedido.Id, Nome = nomeItem, Quantidade = 1, PrecoUnitario = 38m, Subtotal = 38m };
        var frete = new PedidoItem { Id = Guid.NewGuid(), PedidoId = pedido.Id, Nome = "Frete", Quantidade = 1, PrecoUnitario = 7m, Subtotal = 7m };
        return new PedidoReservado(pedido, storefront, [item], frete, 45m, janela);
    }

    [Fact]
    public void ItemComoNoCardapioEEntregaComDiaEHorarioDaJanela()
    {
        var janela = JanelaEntrega.Criar(Guid.NewGuid(), 5, new TimeOnly(12, 0), new TimeOnly(14, 0), 5, "Almoço");

        var texto = ResumoPedidoConversa.Texto(
            Reservado("nhoque artesanal 500 g", janela), SextaFeira, TrocarFormaPagamentoPedidoUseCase.FormaNaEntrega, "Resumo do seu pedido:");

        texto.Should().Contain("- 1x Nhoque Artesanal 500 g: R$")
            .And.Contain("\nEntrega: sex 09/10, das 12:00 às 14:00\n");
    }

    [Fact]
    public void NomeJaCapitalizadoFicaComoCadastrado()
    {
        var texto = ResumoPedidoConversa.Texto(
            Reservado("Bolo de Cenoura da Vó", null), SextaFeira, TrocarFormaPagamentoPedidoUseCase.FormaNaEntrega, "Resumo:");

        texto.Should().Contain("- 1x Bolo de Cenoura da Vó: R$").And.Contain("\nEntrega: sex 09/10\n");
    }

    [Fact]
    public void EncomendaFutura_ExplicaSinalESaldoMesmoComFormaNaEntrega()
    {
        var reservado = Reservado("Bolo de Cenoura", null);
        reservado.Pedido.AgendadoParaEm = reservado.Pedido.CriadoEm.AddDays(2);

        var texto = ResumoPedidoConversa.Texto(reservado, SextaFeira,
            TrocarFormaPagamentoPedidoUseCase.FormaNaEntrega, "Resumo:");

        // #2007 (item 5): forma na entrega não tem link; o sinal é recebido antes da produção.
        texto.Should().Contain("Sinal antes da produção: R$ 22,50 (Pix, dinheiro ou maquininha)")
            .And.Contain("Restante na entrega: R$ 22,50")
            .And.NotContain("link");
    }

    [Fact]
    public void EncomendaFutura_ComFormaOnline_CobraOSinalPeloLink()
    {
        var reservado = Reservado("Bolo de Cenoura", null);
        reservado.Pedido.AgendadoParaEm = reservado.Pedido.CriadoEm.AddDays(2);

        var texto = ResumoPedidoConversa.Texto(reservado, SextaFeira,
            TrocarFormaPagamentoPedidoUseCase.FormaOnline, "Resumo:");

        texto.Should().Contain("Sinal agora por link: R$ 22,50").And.Contain("Restante na entrega: R$ 22,50");
    }

    // #2871: fora da janela o resumo vai como parâmetro do modelo pedido_atualizado, que a Meta recusa com quebra
    // de linha ou com mais de quatro espaços seguidos.
    [Fact]
    public void EmUmaLinha_JuntaAsLinhasSemQuebraEPontuaOsParagrafos()
    {
        const string texto = "Resumo do seu pedido:\n- 2x Brigadeiro (sem granulado): R$ 20,00\nFrete: R$ 5,00\nTotal: R$ 25,00"
            + "\nPagamento: link online (Pix ou cartão)\n\nPague por este link, válido por 30 minutos: https://mp.test/pref-1"
            + "\n\nObrigado pelo pedido!";

        ResumoPedidoConversa.EmUmaLinha(texto).Should().Be(
            "Resumo do seu pedido: 2x Brigadeiro (sem granulado): R$ 20,00; Frete: R$ 5,00; Total: R$ 25,00; "
            + "Pagamento: link online (Pix ou cartão). Pague por este link, válido por 30 minutos: https://mp.test/pref-1 "
            + "Obrigado pelo pedido!");
    }

    [Fact]
    public void EmUmaLinha_LongoDemaisOmiteOsUltimosItensEMantemTotalLinkEAgradecimento()
    {
        // Revisão da #2874: cortar o fim levava o link de pagamento e o agradecimento; saem os últimos itens.
        var longo = "Resumo do seu pedido:\n" + string.Join("\n", Enumerable.Range(1, 80).Select(i => $"- 1x Item     {i}: R$ 1,00"))
            + "\nTotal: R$ 80,00\nPagamento: link online (Pix ou cartão)"
            + "\n\nPague por este link, válido por 30 minutos: https://mp.test/pref-1\n\nObrigado pelo pedido!";

        var linha = ResumoPedidoConversa.EmUmaLinha(longo);

        linha.Should().NotContain("\n").And.NotContain("  ").And.StartWith("Resumo do seu pedido: 1x Item 1: R$ 1,00; ")
            .And.Contain("; e mais ").And.Contain(" itens; Total: R$ 80,00; Pagamento: link online (Pix ou cartão).")
            .And.EndWith("https://mp.test/pref-1 Obrigado pelo pedido!");
        linha.Length.Should().BeLessThanOrEqualTo(ResumoPedidoConversa.TamanhoMaximoEmUmaLinha);
    }

    [Fact]
    public void EmUmaLinha_SemItensParaOmitirCortaNoLimiteSemPartirEmoji()
    {
        var longo = "Ob: " + string.Concat(Enumerable.Repeat("\U0001F600", 600));

        var linha = ResumoPedidoConversa.EmUmaLinha(longo);

        linha.Should().EndWith("…");
        linha.Length.Should().BeLessThanOrEqualTo(ResumoPedidoConversa.TamanhoMaximoEmUmaLinha);
        char.IsHighSurrogate(linha[^2]).Should().BeFalse("o corte não parte um emoji ao meio");
    }
}
