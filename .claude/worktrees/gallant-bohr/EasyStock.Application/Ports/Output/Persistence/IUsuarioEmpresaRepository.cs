using EasyStock.Domain.Entities;

namespace EasyStock.Application.Ports.Output.Persistence
{
    public interface IUsuarioEmpresaRepository
    {
        Task AddAsync(UsuarioEmpresa usuarioEmpresa);
        Task<UsuarioEmpresa?> GetByUsuarioEEmpresaAsync(Guid usuarioId, Guid empresaId);
        Task UpdateAsync(UsuarioEmpresa usuarioEmpresa);
    }
}
