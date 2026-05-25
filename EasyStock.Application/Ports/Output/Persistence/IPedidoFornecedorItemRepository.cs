using EasyStock.Domain.Entities;

namespace EasyStock.Application.Ports.Output.Persistence
{
    public interface IPedidoFornecedorItemRepository
    {
        Task<IEnumerable<PedidoFornecedorItem>> GetByPedidoIdAsync(
            Guid pedidoId,
            CancellationToken ct = default);

        Task<PedidoFornecedorItem?> GetByIdAsync(
            Guid itemId,
            CancellationToken ct = default);

        Task InsertAsync(PedidoFornecedorItem item, CancellationToken ct = default);

        Task UpdateAsync(PedidoFornecedorItem item, CancellationToken ct = default);

        Task<bool> ExisteAsync(Guid itemId, CancellationToken ct = default);
    }
}
