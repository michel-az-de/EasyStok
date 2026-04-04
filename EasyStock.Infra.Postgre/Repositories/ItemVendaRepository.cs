using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Entities;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Infra.Postgre.Repositories
{
    public sealed class ItemVendaRepository(EasyStockDbContext dbContext)
        : BaseRepository<ItemVenda>(dbContext), IItemVendaRepository
    {
    }
}
