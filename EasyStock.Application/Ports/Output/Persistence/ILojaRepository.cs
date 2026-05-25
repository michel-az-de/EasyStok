using EasyStock.Domain.Entities;

namespace EasyStock.Application.Ports.Output.Persistence
{
    public interface ILojaRepository
    {
        Task<Loja?> GetByIdAsync(Guid id);
        Task<Loja?> GetByIdAsync(Guid empresaId, Guid id);
        Task<IEnumerable<Loja>> GetByEmpresaAsync(Guid empresaId);
        Task<int> CountByEmpresaAsync(Guid empresaId);
        Task AddAsync(Loja loja);
        Task UpdateAsync(Loja loja);
        Task<IEnumerable<Loja>> SearchAsync(Guid empresaId, string termo, int maxResults = 20);
    }
}
