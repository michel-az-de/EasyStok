using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Infra.Postgre.Data;
namespace EasyStock.Infra.Postgre.Repositories
{
    public sealed class UsuarioPerfilRepository(EasyStockDbContext dbContext) : IUsuarioPerfilRepository
    {
        public Task AddAsync(UsuarioPerfil usuarioPerfil) =>
            dbContext.UsuariosPerfis.AddAsync(usuarioPerfil).AsTask();
        public Task<UsuarioPerfil?> GetByUsuarioEmpresaEPerfilAsync(Guid usuarioId, Guid empresaId, Guid perfilId) =>
            dbContext.UsuariosPerfis.FirstOrDefaultAsync(x =>
                x.UsuarioId == usuarioId &&
                x.EmpresaId == empresaId &&
                x.PerfilId == perfilId);
        public async Task<IReadOnlyList<UsuarioPerfil>> ListarPorUsuarioEEmpresaAsync(Guid usuarioId, Guid empresaId) =>
            await dbContext.UsuariosPerfis
                .Include(x => x.Perfil)
                .Where(x => x.UsuarioId == usuarioId && x.EmpresaId == empresaId)
                .ToListAsync();
        public Task UpdateAsync(UsuarioPerfil usuarioPerfil)
        {
            dbContext.UsuariosPerfis.Update(usuarioPerfil);
            return Task.CompletedTask;
        }
        public Task RemoveAsync(UsuarioPerfil usuarioPerfil)
        {
            dbContext.UsuariosPerfis.Remove(usuarioPerfil);
            return Task.CompletedTask;
        }
    }
}
