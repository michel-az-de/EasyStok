using EasyStock.Domain.Entities;

namespace EasyStock.Application.Ports.Output.Persistence
{
    public interface IProdutoEmbalagemRepository
    {
        Task<IEnumerable<ProdutoEmbalagem>> GetByProdutoAsync(Guid empresaId, Guid produtoId);
        Task InsertAsync(ProdutoEmbalagem embalagem);
        Task UpdateAsync(ProdutoEmbalagem embalagem);
        Task DeleteAsync(Guid empresaId, Guid id);
        Task DeleteByProdutoAsync(Guid empresaId, Guid produtoId);
    }
}
