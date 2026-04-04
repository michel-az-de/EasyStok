using EasyStock.Domain.Entities;

namespace EasyStock.Application.Ports.Output.Persistence
{
    public interface IUsuarioRepository
    {
        Task<Usuario?> GetByIdAsync(Guid id);
        Task<Usuario?> GetByEmailAsync(string email);
        Task<(IEnumerable<Usuario> Usuarios, int Total)> GetByEmpresaAsync(Guid empresaId, int page, int pageSize);
        Task AddAsync(Usuario usuario);
        Task UpdateAsync(Usuario usuario);
    }
}
