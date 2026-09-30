namespace EasyStock.Application.Ports.Output.Atendimento;

/// <summary>
/// Nomes dos eventos de operação publicados no SSE do console (<c>GET api/operacao/eventos</c>, S18).
/// São eventos de UI, publicados depois do commit; não substituem o outbox de integração.
/// </summary>
public static class EventosOperacao
{
    public const string PedidoPago = "pedido.pago";
    public const string PedidoMudouStatus = "pedido.mudou_status";
    public const string CardapioItemComInteresse = "cardapio.item_com_interesse";
    public const string PedidoAtrasado = "pedido.atrasado";
}

/// <summary>Payload de <see cref="EventosOperacao.PedidoPago"/>: o console toca o som e acende o sinal verde.</summary>
/// <param name="Numero">Número curto do pedido (8 primeiros caracteres do id, maiúsculos), o mesmo que o cliente vê.</param>
/// <param name="Janela">Instante agendado para a entrega (<c>Pedido.AgendadoParaEm</c>); nulo quando é para já.</param>
public sealed record PedidoPagoOperacao(Guid PedidoId, string Numero, string? Cliente, decimal Total, DateTime? Janela);

/// <summary>Payload de <see cref="EventosOperacao.PedidoMudouStatus"/>: a cozinha move o card sem recarregar.</summary>
public sealed record PedidoMudouStatusOperacao(Guid PedidoId, string StatusAntigo, string StatusNovo);

/// <summary>
/// Payload de <see cref="EventosOperacao.CardapioItemComInteresse"/> (S31): o item voltou e há
/// <paramref name="Quantidade"/> clientes com interesse aberto; a dona decide se avisa.
/// </summary>
public sealed record CardapioItemComInteresseOperacao(Guid CardapioItemId, int Quantidade);

/// <summary>Payload de <see cref="EventosOperacao.PedidoAtrasado"/> (S21): o card vira "Atrasado" (cor e rótulo, RN-30).</summary>
/// <param name="Numero">Número curto do pedido, o mesmo de <see cref="PedidoPagoOperacao.Numero"/>.</param>
/// <param name="InicioPrevistoEm">Instante (UTC) em que o preparo deveria ter começado.</param>
public sealed record PedidoAtrasadoOperacao(Guid PedidoId, string Numero, string? Cliente, DateTime InicioPrevistoEm);
