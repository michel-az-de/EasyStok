using EasyStock.Application.Services.Pedidos;
using EasyStock.Application.UseCases.Pedidos.Cobranca;

namespace EasyStock.Application.Tests.Services.Pedidos;

/// <summary>
/// #2018 (PEDIDO-08, comanda-14): "online x na entrega" e o sinal da encomenda tinham duas <c>CobrarAsync</c> que divergiam
/// (console x link do cardápio). Agora é uma regra, com o ponto em que elas diferem escrito e testado.
/// </summary>
public class CobrancaDoPedidoReservadoTests
{
    private const string Online = TrocarFormaPagamentoPedidoUseCase.FormaOnline;
    private const string NaEntrega = TrocarFormaPagamentoPedidoUseCase.FormaNaEntrega;

    private static Pedido Pedido(bool encomenda)
    {
        var pedido = EasyStock.Domain.Entities.Pedido.Criar(Guid.NewGuid());
        pedido.AgendadoParaEm = encomenda ? pedido.CriadoEm.AddDays(2) : pedido.CriadoEm;
        return pedido;
    }

    [Theory]
    [InlineData(null, Online)]
    [InlineData("", Online)]
    [InlineData("  ", Online)]
    [InlineData("online", Online)]
    [InlineData(" ONLINE ", Online)]
    [InlineData("na_entrega", NaEntrega)]
    [InlineData("Na_Entrega", NaEntrega)]
    public void NormalizarForma_AceitaOnlineENaEntrega(string? entrada, string esperado) =>
        CobrancaDoPedidoReservado.NormalizarForma(entrada).Should().Be(esperado);

    [Theory]
    [InlineData("boleto")]
    [InlineData("pix")]
    public void NormalizarForma_RecusaOutraForma(string entrada)
    {
        var agir = () => CobrancaDoPedidoReservado.NormalizarForma(entrada);

        agir.Should().Throw<UseCaseValidationException>().WithMessage("*'online' ou 'na_entrega'*");
    }

    [Theory]
    // Console: a atendente combina o sinal; "na entrega" segue sempre na entrega.
    [InlineData(Online, QuemCobra.Atendente, false, Online)]
    [InlineData(Online, QuemCobra.Atendente, true, Online)]
    [InlineData(NaEntrega, QuemCobra.Atendente, false, NaEntrega)]
    [InlineData(NaEntrega, QuemCobra.Atendente, true, NaEntrega)]
    // Link do cardápio: sem ninguém para combinar, a encomenda "na entrega" cobra o sinal pelo link.
    [InlineData(Online, QuemCobra.ClientePeloLink, false, Online)]
    [InlineData(Online, QuemCobra.ClientePeloLink, true, Online)]
    [InlineData(NaEntrega, QuemCobra.ClientePeloLink, false, NaEntrega)]
    [InlineData(NaEntrega, QuemCobra.ClientePeloLink, true, Online)]
    public void FormaEfetiva_SoDifereNoLinkDeEncomendaNaEntrega(string forma, QuemCobra quem, bool encomenda, string esperada) =>
        CobrancaDoPedidoReservado.FormaEfetiva(forma, Pedido(encomenda), quem).Should().Be(esperada);

    [Theory]
    [InlineData(100, 100, 50)] // nada pago: o sinal inteiro
    [InlineData(100, 70, 20)] // 30 pagos de 50: faltam 20
    [InlineData(100, 50, 0)] // sinal já pago
    [InlineData(100, 30, 0)] // pagou além do sinal
    [InlineData(100, 40, 0)]
    [InlineData(40, 10, 0)] // saldo menor que o sinal restante nunca passa do saldo
    public void SinalRestante_FaltaDoSinalSemPassarDoSaldoNemFicarNegativo(decimal total, decimal saldo, decimal esperado) =>
        PlanoPagamentoEncomenda.SinalRestante(total, saldo).Should().Be(esperado);
}
