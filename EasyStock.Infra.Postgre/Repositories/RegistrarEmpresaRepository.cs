using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Entities;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Infra.Postgre.Repositories
{
    public sealed class RegistrarEmpresaRepository(EasyStockDbContext dbContext) : IRegistrarEmpresaRepository
    {
        public Task AddEmpresaAsync(Empresa empresa) =>
            dbContext.Empresas.AddAsync(empresa).AsTask();

        public Task AddUsuarioEmpresaAsync(UsuarioEmpresa usuarioEmpresa) =>
            dbContext.UsuariosEmpresas.AddAsync(usuarioEmpresa).AsTask();

        public Task AddUsuarioPerfilAsync(UsuarioPerfil usuarioPerfil) =>
            dbContext.UsuariosPerfis.AddAsync(usuarioPerfil).AsTask();
    }
}
