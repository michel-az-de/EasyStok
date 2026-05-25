namespace EasyStock.Domain.Exceptions.Storefront;

/// <summary>
/// Pedido já possui uma avaliação registrada (UNIQUE PedidoId).
/// Mapeado para HTTP 409 Conflict.
/// </summary>
public sealed class AvaliacaoDuplicadaException(Guid avaliacaoId)
    : Exception($"Este pedido já foi avaliado.")
{
    /// <summary>ID da avaliação existente para exibir link no cliente.</summary>
    public Guid AvaliacaoId { get; } = avaliacaoId;
}
