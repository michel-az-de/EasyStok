using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Entities;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Infra.Postgre.Repositories
{
    public sealed class VendaRepository(EasyStockDbContext dbContext)
        : BaseRepository<Venda>(dbContext), IVendaRepository
    {
    }
}
