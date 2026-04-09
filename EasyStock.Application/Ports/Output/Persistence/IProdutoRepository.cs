using EasyStock.Domain.Entities;

namespace EasyStock.Application.Ports.Output.Persistence
{
    public interface IProdutoRepository
    {
        Task<Produto?> GetByIdAsync(Guid id);
        Task<Produto?> GetByIdAsync(Guid empresaId, Guid id);
        Task<Produto?> GetDetalheAsync(Guid empresaId, Guid id);
        Task<bool> ExistsSkuBaseAsync(Guid empresaId, string skuBase, Guid? ignoreProdutoId = null);
        Task<IEnumerable<Produto>> SearchAsync(Guid empresaId, string termo, int maxResults = 100);
        Task<(IEnumerable<Produto> Produtos, int TotalCount)> GetProdutosPaginadosAsync(Guid empresaId, int page = 1, int pageSize = 20, string? sort = "nome", string? order = "asc");
        Task InsertAsync(Produto produto);
        Task UpdateAsync(Produto produto);
    }
}
