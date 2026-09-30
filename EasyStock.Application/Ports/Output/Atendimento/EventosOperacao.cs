namespace EasyStock.Application.Ports.Output.Atendimento;

/// <summary>
/// Nomes dos eventos de operação publicados no SSE do console (<c>GET api/operacao/eventos</c>, S18).
/// São eventos de UI, publicados depois do commit; não substituem o outbox de integração.
/// </summary>
public static class EventosOperacao
{
    public const string PedidoPago = "pedido.pago";
    public const string PedidoMudouStatus = "pedido.mudou_status";
}

/// <summary>Payload de <see cref="EventosOperacao.PedidoPago"/>: o console toca o som e acende o sinal verde.</summary>
/// <param name="Numero">Número curto do pedido (8 primeiros caracteres do id, maiúsculos), o mesmo que o cliente vê.</param>
/// <param name="Janela">Instante agendado para a entrega (<c>Pedido.AgendadoParaEm</c>); nulo quando é para já.</param>
public sealed record PedidoPagoOperacao(Guid PedidoId, string Numero, string? Cliente, decimal Total, DateTime? Janela);

/// <summary>Payload de <see cref="EventosOperacao.PedidoMudouStatus"/>: a cozinha move o card sem recarregar.</summary>
public sealed record PedidoMudouStatusOperacao(Guid PedidoId, string StatusAntigo, string StatusNovo);
