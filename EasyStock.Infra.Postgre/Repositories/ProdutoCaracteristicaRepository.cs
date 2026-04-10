using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Entities;
using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Infra.Postgre.Repositories
{
    public sealed class ProdutoCaracteristicaRepository(EasyStockDbContext dbContext)
        : IProdutoCaracteristicaRepository
    {
        public async Task<IEnumerable<ProdutoCaracteristica>> GetByProdutoAsync(Guid empresaId, Guid produtoId) =>
            await dbContext.ProdutosCaracteristica
                .AsNoTracking()
                .Where(c => c.EmpresaId == empresaId && c.ProdutoId == produtoId)
                .OrderBy(c => c.OrdemExibicao)
                .ToListAsync();

        public Task InsertAsync(ProdutoCaracteristica caracteristica) =>
            dbContext.ProdutosCaracteristica.AddAsync(caracteristica).AsTask();

        public Task UpdateAsync(ProdutoCaracteristica caracteristica)
        {
            dbContext.ProdutosCaracteristica.Update(caracteristica);
            return Task.CompletedTask;
        }

        public Task DeleteAsync(Guid id)
        {
            dbContext.ProdutosCaracteristica.Remove(new ProdutoCaracteristica { Id = id });
            return Task.CompletedTask;
        }
    }
}
