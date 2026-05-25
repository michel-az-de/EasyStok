using EasyStock.Domain.Entities.Storefront;

namespace EasyStock.Application.Ports.Output.Persistence.Storefront;

public interface IPedidoAvaliacaoRepository
{
    Task<PedidoAvaliacao?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<PedidoAvaliacao?> GetByPedidoAsync(Guid pedidoId, CancellationToken ct = default);
    Task<IReadOnlyList<PedidoAvaliacao>> GetVisiveisDaEmpresaAsync(Guid empresaId, int max = 50, CancellationToken ct = default);
    Task AddAsync(PedidoAvaliacao avaliacao, CancellationToken ct = default);
    Task UpdateAsync(PedidoAvaliacao avaliacao, CancellationToken ct = default);
}
