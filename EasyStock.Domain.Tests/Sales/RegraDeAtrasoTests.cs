using EasyStock.Domain.Entities;
using EasyStock.Domain.Sales;
using FluentAssertions;

namespace EasyStock.Domain.Tests.Sales;

/// <summary>#3294: atrasado é o pedido marcado para uma hora que passou e ainda em aberto; pedido para agora nunca atrasa.</summary>
public class RegraDeAtrasoTests
{
    private static readonly DateTime Agora = new(2026, 11, 24, 15, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(StatusPedidoMapper.Aguardando, -1, true)]
    [InlineData(StatusPedidoMapper.Preparando, -60, true)]
    [InlineData(StatusPedidoMapper.SaiuParaEntrega, -5, true)]
    [InlineData(StatusPedidoMapper.AguardandoPagamento, -5, true)]
    [InlineData(StatusPedidoMapper.Aguardando, 30, false)]
    [InlineData(StatusPedidoMapper.Entregue, -60, false)]
    [InlineData(StatusPedidoMapper.Cancelado, -60, false)]
    [InlineData(StatusPedidoMapper.Rascunho, -60, false)]
    public void AtrasaSoOAbertoComHoraMarcadaVencida(string status, int minutos, bool atrasado)
    {
        var marcado = Agora.AddMinutes(minutos);
        RegraDeAtraso.EstaAtrasado(status, marcado, Agora).Should().Be(atrasado);
        var pedido = new Pedido { Status = status, AgendadoParaEm = marcado };
        RegraDeAtraso.Pedido(Agora).Compile()(pedido).Should().Be(atrasado, "a expressão do banco segue a mesma regra");
    }

    [Fact]
    public void PedidoParaAgoraNuncaAtrasa()
    {
        RegraDeAtraso.EstaAtrasado(StatusPedidoMapper.Aguardando, null, Agora).Should().BeFalse();
        RegraDeAtraso.Pedido(Agora).Compile()(new Pedido { Status = StatusPedidoMapper.Aguardando }).Should().BeFalse();
    }
}
