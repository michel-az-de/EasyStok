using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Entities;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Infra.Postgre.Repositories
{
    public sealed class UsuarioEmpresaRepository(EasyStockDbContext dbContext) : IUsuarioEmpresaRepository
    {
        public Task AddAsync(UsuarioEmpresa usuarioEmpresa) =>
            dbContext.UsuariosEmpresas.AddAsync(usuarioEmpresa).AsTask();
    }
}
