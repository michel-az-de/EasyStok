using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Entities;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Infra.Postgre.Repositories
{
    public sealed class ProdutoEmbalagemRepository(EasyStockDbContext dbContext)
        : IProdutoEmbalagemRepository
    {
        public Task InsertAsync(ProdutoEmbalagem embalagem) =>
            dbContext.ProdutosEmbalagem.AddAsync(embalagem).AsTask();
    }
}
