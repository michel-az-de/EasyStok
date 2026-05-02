using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Entities;
using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore;
namespace EasyStock.Infra.Postgre.Repositories
{
    public sealed class UsuarioEmpresaRepository(EasyStockDbContext dbContext) : IUsuarioEmpresaRepository
    {
        public Task AddAsync(UsuarioEmpresa usuarioEmpresa) =>
            dbContext.UsuariosEmpresas.AddAsync(usuarioEmpresa).AsTask();
        public Task<UsuarioEmpresa?> GetByUsuarioEEmpresaAsync(Guid usuarioId, Guid empresaId) =>
            dbContext.UsuariosEmpresas.FirstOrDefaultAsync(x => x.UsuarioId == usuarioId && x.EmpresaId == empresaId);
        public async Task<IReadOnlyList<UsuarioEmpresa>> GetByUsuarioIdAsync(Guid usuarioId) =>
            await dbContext.UsuariosEmpresas
                .AsNoTracking()
                .Include(ue => ue.Empresa)
                .Where(ue => ue.UsuarioId == usuarioId)
                .ToListAsync();
        public Task UpdateAsync(UsuarioEmpresa usuarioEmpresa)
        {
            dbContext.UsuariosEmpresas.Update(usuarioEmpresa);
            return Task.CompletedTask;
        }
    }
}