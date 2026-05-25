using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Entities;
using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Infra.Postgre.Repositories
{
    public sealed class AnuncioIaRepository(EasyStockDbContext dbContext) : IAnuncioIaRepository
    {
        public Task<AnuncioIa?> GetByIdAsync(Guid empresaId, Guid id) =>
            dbContext.AnunciosIa
                .FirstOrDefaultAsync(a => a.EmpresaId == empresaId && a.Id == id);

        public async Task<IReadOnlyList<AnuncioIa>> GetByProdutoAsync(Guid empresaId, Guid produtoId)
        {
            return await dbContext.AnunciosIa
                .AsNoTracking()
                .Where(a => a.EmpresaId == empresaId && a.ProdutoId == produtoId && a.Salvo)
                .OrderByDescending(a => a.CriadoEm)
                .ToListAsync();
        }

        public Task AddAsync(AnuncioIa anuncio) =>
            dbContext.AnunciosIa.AddAsync(anuncio).AsTask();

        public Task UpdateAsync(AnuncioIa anuncio)
        {
            dbContext.AnunciosIa.Update(anuncio);
            return Task.CompletedTask;
        }

        public Task RemoveAsync(AnuncioIa anuncio)
        {
            dbContext.AnunciosIa.Remove(anuncio);
            return Task.CompletedTask;
        }
    }
}
