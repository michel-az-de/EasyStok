using EasyStock.Domain.Entities;

namespace EasyStock.Application.Ports.Output.Persistence
{
    public interface ICategoriaRepository
    {
        Task<Categoria?> GetByIdAsync(Guid id);
        Task<IEnumerable<Categoria>> GetByEmpresaAsync(Guid empresaId);
        Task<bool> ExisteProdutosNaCategoriaAsync(Guid categoriaId);
        Task AddAsync(Categoria categoria);
        Task UpdateAsync(Categoria categoria);
        Task DeleteAsync(Guid id);
    }
}
