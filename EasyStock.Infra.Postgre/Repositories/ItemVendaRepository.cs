using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Infra.Postgre.Repositories
{
    public sealed class ItemVendaRepository(EasyStockDbContext dbContext)
        : IItemVendaRepository
    {
        public Task InsertAsync(ItemVenda itemVenda) =>
            dbContext.ItensVenda.AddAsync(itemVenda).AsTask();

        public Task InsertRangeAsync(IEnumerable<ItemVenda> itens)
        {
            dbContext.ItensVenda.AddRange(itens);
            return Task.CompletedTask;
        }
    }
}
