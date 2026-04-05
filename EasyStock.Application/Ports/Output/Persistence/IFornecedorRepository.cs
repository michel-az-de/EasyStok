using EasyStock.Domain.Entities;

namespace EasyStock.Application.Ports.Output.Persistence
{
    public interface IFornecedorRepository
    {
        Task<Fornecedor?> GetByIdAsync(Guid id);
        Task<Fornecedor?> GetByIdAsync(Guid empresaId, Guid id);
        Task<(IEnumerable<Fornecedor>, int total)> GetByEmpresaAsync(Guid empresaId, int page, int pageSize, bool? ativo = null, string? search = null);
        Task AddAsync(Fornecedor fornecedor);
        Task UpdateAsync(Fornecedor fornecedor);
    }
}
