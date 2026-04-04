using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Entities;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Infra.Postgre.Repositories
{
    public sealed class UsuarioPerfilRepository(EasyStockDbContext dbContext) : IUsuarioPerfilRepository
    {
        public Task AddAsync(UsuarioPerfil usuarioPerfil) =>
            dbContext.UsuariosPerfis.AddAsync(usuarioPerfil).AsTask();

        public Task<UsuarioPerfil?> GetByUsuarioEmpresaEPerfilAsync(Guid usuarioId, Guid empresaId, Guid perfilId) =>
            Task.FromResult(dbContext.UsuariosPerfis.FirstOrDefault(x =>
                x.UsuarioId == usuarioId &&
                x.EmpresaId == empresaId &&
                x.PerfilId == perfilId));

        public Task UpdateAsync(UsuarioPerfil usuarioPerfil)
        {
            dbContext.UsuariosPerfis.Update(usuarioPerfil);
            return Task.CompletedTask;
        }
    }
}
