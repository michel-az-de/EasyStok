using EasyStock.Domain.Entities;

namespace EasyStock.Application.Ports.Output.Persistence
{
    public interface IProdutoVariacaoRepository
    {
        Task<ProdutoVariacao?> GetByIdAsync(Guid id);
        Task<ProdutoVariacao?> GetByIdAsync(Guid empresaId, Guid produtoId, Guid id);
        Task<IEnumerable<ProdutoVariacao>> GetByProdutoAsync(Guid empresaId, Guid produtoId);
        Task<bool> ExistsSkuAsync(Guid empresaId, string sku, Guid? ignoreVariacaoId = null);
        Task<IEnumerable<ProdutoVariacao>> SearchAsync(Guid empresaId, string termo, int maxResults = 20);
        Task InsertAsync(ProdutoVariacao variacao);
        Task UpdateAsync(ProdutoVariacao variacao);
        Task DeleteAsync(Guid empresaId, Guid id);
        Task DeleteByProdutoAsync(Guid empresaId, Guid produtoId);
    }
}
