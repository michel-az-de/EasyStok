using EasyStock.Domain.Entities;

namespace EasyStock.Application.Ports.Output.Persistence
{
    public interface IItemVendaRepository
    {
        Task InsertAsync(ItemVenda itemVenda);
        Task InsertRangeAsync(IEnumerable<ItemVenda> itens);
    }
}
