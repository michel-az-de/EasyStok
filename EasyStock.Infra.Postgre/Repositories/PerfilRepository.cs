using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Entities;
using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Infra.Postgre.Repositories
{
    public sealed class PerfilRepository(EasyStockDbContext dbContext) : IPerfilRepository
    {
        public Task<Perfil?> GetByIdAsync(Guid id) =>
            dbContext.Perfis
                .Include(p => p.Permissoes)
                .FirstOrDefaultAsync(p => p.Id == id);

        public async Task<IEnumerable<Perfil>> GetPadroesAsync() =>
            await dbContext.Perfis
                .AsNoTracking()
                .Include(p => p.Permissoes)
                .Where(p => p.EmpresaId == null)
                .ToListAsync();

        public async Task<IEnumerable<Perfil>> GetByEmpresaAsync(Guid empresaId) =>
            await dbContext.Perfis
                .AsNoTracking()
                .Include(p => p.Permissoes)
                .Where(p => p.EmpresaId == empresaId)
                .ToListAsync();

        public Task AddAsync(Perfil perfil) =>
            dbContext.Perfis.AddAsync(perfil).AsTask();
    }
}
