namespace EasyStock.Application.Ports.Output.Atendimento;

/// <summary>
/// Nomes dos eventos de operação publicados no SSE do console (<c>GET api/operacao/eventos</c>, S18).
/// São eventos de UI, publicados depois do commit; não substituem o outbox de integração.
/// </summary>
public static class EventosOperacao
{
    public const string PedidoPago = "pedido.pago";
    public const string PedidoMudouStatus = "pedido.mudou_status";
    public const string ImpressaoPendente = "impressao.pendente";
    public const string ImpressaoAtrasada = "impressao.atrasada";
    public const string PedidoAtrasado = "pedido.atrasado";
    public const string EstoqueDesacertoResolvido = "estoque.desacerto_resolvido";
}

/// <summary>Payload de <see cref="EventosOperacao.PedidoPago"/>: o console toca o som e acende o sinal verde.</summary>
/// <param name="Numero">Número curto do pedido (8 primeiros caracteres do id, maiúsculos), o mesmo que o cliente vê.</param>
/// <param name="Janela">Instante agendado para a entrega (<c>Pedido.AgendadoParaEm</c>); nulo quando é para já.</param>
public sealed record PedidoPagoOperacao(Guid PedidoId, string Numero, string? Cliente, decimal Total, DateTime? Janela);

/// <summary>Payload de <see cref="EventosOperacao.PedidoMudouStatus"/>: a cozinha move o card sem recarregar.</summary>
public sealed record PedidoMudouStatusOperacao(Guid PedidoId, string StatusAntigo, string StatusNovo);

/// <summary>Payload de <see cref="EventosOperacao.ImpressaoPendente"/> (S20): a aba do console que imprime busca a fila.</summary>
public sealed record ImpressaoPendenteOperacao(Guid ImpressaoId, Guid PedidoId);

/// <summary>Payload de <see cref="EventosOperacao.ImpressaoAtrasada"/> (S20): canhoto parado na fila, o console avisa a dona.</summary>
public sealed record ImpressaoAtrasadaOperacao(Guid ImpressaoId, Guid PedidoId, DateTime CriadaEm);

/// <summary>Payload de <see cref="EventosOperacao.PedidoAtrasado"/> (S21): o card vira "Atrasado" (cor e rótulo, RN-30).</summary>
/// <param name="Numero">Número curto do pedido, o mesmo de <see cref="PedidoPagoOperacao.Numero"/>.</param>
/// <param name="InicioPrevistoEm">Instante (UTC) em que o preparo deveria ter começado.</param>
public sealed record PedidoAtrasadoOperacao(Guid PedidoId, string Numero, string? Cliente, DateTime InicioPrevistoEm);

/// <summary>Payload de <see cref="EventosOperacao.EstoqueDesacertoResolvido"/> (S22): o alerta do produto some do console.</summary>
public sealed record EstoqueDesacertoResolvidoOperacao(Guid ProdutoId, decimal QuantidadeAtual);
