using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Entities;
using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Infra.Postgre.Repositories
{
    public sealed class PlanoRepository(EasyStockDbContext dbContext) : IPlanoRepository
    {
        public Task<Plano?> GetByIdAsync(Guid id) =>
            dbContext.Planos.FirstOrDefaultAsync(p => p.Id == id);

        public async Task<IEnumerable<Plano>> GetAtivosAsync() =>
            await dbContext.Planos
                .AsNoTracking()
                .Where(p => p.Ativo)
                .ToListAsync();

        public Task AddAsync(Plano plano) =>
            dbContext.Planos.AddAsync(plano).AsTask();
    }
}
