using EasyStock.Domain.Entities;

namespace EasyStock.Application.Ports.Output.Persistence
{
    public interface IRegistrarEmpresaRepository
    {
        Task AddEmpresaAsync(Empresa empresa);
        Task AddUsuarioEmpresaAsync(UsuarioEmpresa usuarioEmpresa);
        Task AddUsuarioPerfilAsync(UsuarioPerfil usuarioPerfil);
    }
}
