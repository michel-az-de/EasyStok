using EasyStock.Domain.Entities;

namespace EasyStock.Application.Ports.Output.Persistence
{
    public interface IFornecedorRepository
    {
        Task<Fornecedor?> GetByIdAsync(Guid id);
        Task<(IEnumerable<Fornecedor>, int total)> GetByEmpresaAsync(Guid empresaId, int page, int pageSize);
        Task AddAsync(Fornecedor fornecedor);
        Task UpdateAsync(Fornecedor fornecedor);
    }
}
