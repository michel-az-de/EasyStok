using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Entities;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Infra.Postgre.Repositories
{
    public sealed class UsuarioEmpresaRepository(EasyStockDbContext dbContext) : IUsuarioEmpresaRepository
    {
        public Task AddAsync(UsuarioEmpresa usuarioEmpresa) =>
            dbContext.UsuariosEmpresas.AddAsync(usuarioEmpresa).AsTask();

        public Task<UsuarioEmpresa?> GetByUsuarioEEmpresaAsync(Guid usuarioId, Guid empresaId) =>
            Task.FromResult(dbContext.UsuariosEmpresas.FirstOrDefault(x => x.UsuarioId == usuarioId && x.EmpresaId == empresaId));

        public Task UpdateAsync(UsuarioEmpresa usuarioEmpresa)
        {
            dbContext.UsuariosEmpresas.Update(usuarioEmpresa);
            return Task.CompletedTask;
        }
    }
}
