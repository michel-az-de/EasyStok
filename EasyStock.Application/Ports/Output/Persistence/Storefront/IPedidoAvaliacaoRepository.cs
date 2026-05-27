using EasyStock.Domain.Entities.Storefront;

namespace EasyStock.Application.Ports.Output.Persistence.Storefront;

public interface IPedidoAvaliacaoRepository
{
    Task<PedidoAvaliacao?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<PedidoAvaliacao?> GetByPedidoAsync(Guid pedidoId, CancellationToken ct = default);

    /// <summary>
    /// Lookup em batch: carrega todas as avaliações associadas a um conjunto de
    /// PedidoIds. Retorno indexado por PedidoId (Dictionary) para permitir
    /// resolução O(1) no caller — anti-N+1 para listagens (ListarPedidosCliente).
    /// </summary>
    Task<IReadOnlyDictionary<Guid, PedidoAvaliacao>> GetByPedidoIdsAsync(
        IReadOnlyCollection<Guid> pedidoIds,
        CancellationToken ct = default);

    Task<IReadOnlyList<PedidoAvaliacao>> GetVisiveisDaEmpresaAsync(Guid empresaId, int max = 50, CancellationToken ct = default);
    Task AddAsync(PedidoAvaliacao avaliacao, CancellationToken ct = default);
    Task UpdateAsync(PedidoAvaliacao avaliacao, CancellationToken ct = default);
}
