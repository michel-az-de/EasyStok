using EasyStock.Domain.Entities;

namespace EasyStock.Application.Ports.Output.Persistence
{
    public interface ILojaRepository
    {
        Task<Loja?> GetByIdAsync(Guid id);
        Task<IEnumerable<Loja>> GetByEmpresaAsync(Guid empresaId);
        Task<int> CountByEmpresaAsync(Guid empresaId);
        Task AddAsync(Loja loja);
        Task UpdateAsync(Loja loja);
    }
}
