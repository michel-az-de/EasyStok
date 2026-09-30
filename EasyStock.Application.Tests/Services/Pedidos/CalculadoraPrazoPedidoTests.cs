using EasyStock.Application.Services.Pedidos;

namespace EasyStock.Application.Tests.Services.Pedidos;

/// <summary>S15 (RN-06, RN-22): o agente nunca promete prazo menor que o preparo mais longo mais o respiro.</summary>
public class CalculadoraPrazoPedidoTests
{
    [Fact]
    public void MaxMaisRespiro()
    {
        var prazo = CalculadoraPrazoPedido.PrazoMinimo(new int?[] { 40, 60 }, tempoPreparoPadraoMinutos: 60, respiroMinutos: 40);

        prazo.Should().Be(100);
    }

    [Fact]
    public void ItemSemTempoUsaPadrao()
    {
        var prazo = CalculadoraPrazoPedido.PrazoMinimo(new int?[] { 30, null }, tempoPreparoPadraoMinutos: 90, respiroMinutos: 40);

        prazo.Should().Be(130);
    }

    [Fact]
    public void SemItensDevolveSoRespiro()
    {
        CalculadoraPrazoPedido.PrazoMinimo(Array.Empty<int?>(), 60, 40).Should().Be(40);
    }
}
