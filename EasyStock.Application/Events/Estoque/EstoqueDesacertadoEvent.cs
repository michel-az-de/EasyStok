namespace EasyStock.Application.Events.Estoque;

/// <summary>
/// Enfileirado no Outbox quando a baixa de estoque de um pedido encontra saldo menor que o
/// pedido (S17 / RN-48): o pedido segue, o saldo vai a zero e a falta vira
/// <c>QuantidadeDescoberta</c> no lote. Consumidores previstos: SSE <c>estoque.desacerto</c>
/// (S18) e ajuste de estoque (S22). TipoEvento no outbox: <see cref="TipoEventoOutbox"/>.
/// </summary>
public sealed record EstoqueDesacertadoEvent(
    Guid EmpresaId,
    Guid? LojaId,
    Guid ProdutoId,
    Guid ItemEstoqueId,
    Guid PedidoId,
    decimal Falta,
    DateTime OcorridoEm)
{
    public const string TipoEventoOutbox = "estoque.desacerto";
}
