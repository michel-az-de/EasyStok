using EasyStock.Domain.Entities;

namespace EasyStock.Application.Ports.Output.Persistence
{
    public interface IAnuncioIaRepository
    {
        Task<AnuncioIa?> GetByIdAsync(Guid empresaId, Guid id);
        Task<IReadOnlyList<AnuncioIa>> GetByProdutoAsync(Guid empresaId, Guid produtoId);
        Task AddAsync(AnuncioIa anuncio);
        Task UpdateAsync(AnuncioIa anuncio);
        Task RemoveAsync(AnuncioIa anuncio);
    }
}
