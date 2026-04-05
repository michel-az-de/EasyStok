using EasyStock.Domain.Entities;

namespace EasyStock.Application.Ports.Output.Persistence;

public interface IItemPedidoFornecedorRepository
{
    Task<IReadOnlyCollection<ItemPedidoFornecedor>> GetByPedidoAsync(Guid pedidoFornecedorId);
    Task AddRangeAsync(IEnumerable<ItemPedidoFornecedor> itens);
    Task RemoveByPedidoAsync(Guid pedidoFornecedorId);
}
