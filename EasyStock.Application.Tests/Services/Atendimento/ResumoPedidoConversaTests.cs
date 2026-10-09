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
}
