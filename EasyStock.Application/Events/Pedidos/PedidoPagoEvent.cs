using EasyStock.Domain.Events;

namespace EasyStock.Application.Events.Pedidos;

/// <summary>
/// Enfileirado no Outbox quando o pagamento online do pedido é confirmado
/// (<see cref="UseCases.Pedidos.Cobranca.ConfirmarPagamentoPedidoUseCase"/>, S11), na mesma transação da
/// confirmação. É o gancho de quem reage ao pagamento: aviso ao cliente (S13), impressão da comanda
/// (S20) e demais passos da esteira (S18). Até eles existirem, só o
/// <see cref="Handlers.PedidoPagoLogHandler"/> consome.
/// </summary>
public sealed record PedidoPagoEvent(
    Guid PedidoId,
    Guid EmpresaId,
    Guid? LojaId,
    Guid? ClienteId,
    Guid? ConversaId,
    Guid CobrancaPedidoId,
    string Provedor,
    string PagamentoExternoId,
    string MetodoPagamento,
    decimal ValorPago,
    string StatusPedido,
    DateTime PagoEm)
    : DomainEvent(Guid.NewGuid(), PagoEm)
{
    /// <summary>TipoEvento no outbox.</summary>
    public const string TipoEvento = "pedido.pago";
}
