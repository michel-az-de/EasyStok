using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Entities;
using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Infra.Postgre.Repositories
{
    public sealed class LojaRepository(EasyStockDbContext dbContext) : ILojaRepository
    {
        public Task<Loja?> GetByIdAsync(Guid id) =>
            dbContext.Lojas.FirstOrDefaultAsync(l => l.Id == id);

        public async Task<IEnumerable<Loja>> GetByEmpresaAsync(Guid empresaId) =>
            await dbContext.Lojas
                .AsNoTracking()
                .Where(l => l.EmpresaId == empresaId)
                .OrderBy(l => l.Nome)
                .ToListAsync();

        public Task<int> CountByEmpresaAsync(Guid empresaId) =>
            dbContext.Lojas.CountAsync(l => l.EmpresaId == empresaId && l.Ativa);

        public Task AddAsync(Loja loja) =>
            dbContext.Lojas.AddAsync(loja).AsTask();

        public Task UpdateAsync(Loja loja)
        {
            dbContext.Lojas.Update(loja);
            return Task.CompletedTask;
        }
    }
}
