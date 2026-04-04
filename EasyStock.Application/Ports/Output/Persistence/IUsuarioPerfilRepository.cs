using EasyStock.Domain.Entities;

namespace EasyStock.Application.Ports.Output.Persistence
{
    public interface IUsuarioPerfilRepository
    {
        Task AddAsync(UsuarioPerfil usuarioPerfil);
        Task UpdateAsync(UsuarioPerfil usuarioPerfil);
    }
}
