using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Entities;
using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Infra.Postgre.Repositories
{
    public sealed class ProdutoEmbalagemRepository(EasyStockDbContext dbContext)
        : IProdutoEmbalagemRepository
    {
        public async Task<IEnumerable<ProdutoEmbalagem>> GetByProdutoAsync(Guid empresaId, Guid produtoId) =>
            await dbContext.ProdutosEmbalagem
                .AsNoTracking()
                .Where(e => e.EmpresaId == empresaId && e.ProdutoId == produtoId)
                .OrderBy(e => e.Nome)
                .ToListAsync();

        public Task InsertAsync(ProdutoEmbalagem embalagem) =>
            dbContext.ProdutosEmbalagem.AddAsync(embalagem).AsTask();

        public Task UpdateAsync(ProdutoEmbalagem embalagem)
        {
            dbContext.ProdutosEmbalagem.Update(embalagem);
            return Task.CompletedTask;
        }

        public Task DeleteAsync(Guid id)
        {
            dbContext.ProdutosEmbalagem.Remove(new ProdutoEmbalagem { Id = id });
            return Task.CompletedTask;
        }
    }
}
