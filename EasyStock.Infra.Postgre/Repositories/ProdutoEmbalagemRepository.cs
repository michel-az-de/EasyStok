using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Infra.Postgre.Data;

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

        public async Task DeleteAsync(Guid empresaId, Guid id)
        {
            // Defesa multi-tenant: só remove se pertencer à empresa.
            var entity = await dbContext.ProdutosEmbalagem
                .FirstOrDefaultAsync(e => e.Id == id && e.EmpresaId == empresaId);
            if (entity is not null)
                dbContext.ProdutosEmbalagem.Remove(entity);
        }

        public async Task DeleteByProdutoAsync(Guid empresaId, Guid produtoId) =>
            await dbContext.ProdutosEmbalagem
                .Where(e => e.EmpresaId == empresaId && e.ProdutoId == produtoId)
                .ExecuteDeleteAsync();
    }
}
