namespace EasyStock.Domain.Exceptions.Storefront;

/// <summary>
/// Pedido não está no status Entregue ou ainda não passaram 24h da entrega.
/// Mapeado para HTTP 422 Unprocessable Entity.
/// </summary>
public sealed class PedidoNaoElegivelParaAvaliacaoException(string message)
    : Exception(message);
