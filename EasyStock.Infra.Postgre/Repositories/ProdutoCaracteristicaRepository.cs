using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Entities;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Infra.Postgre.Repositories
{
    public sealed class ProdutoCaracteristicaRepository(EasyStockDbContext dbContext)
        : IProdutoCaracteristicaRepository
    {
        public Task InsertAsync(ProdutoCaracteristica caracteristica) =>
            dbContext.ProdutosCaracteristica.AddAsync(caracteristica).AsTask();
    }
}
