using EasyStock.Domain.Entities;

namespace EasyStock.Application.Ports.Output.Persistence
{
    public interface IProdutoRepository : IBaseRepository<Produto>
    {
        Task<IEnumerable<Produto>> SearchAsync(Guid empresaId, string termo);
        Task<(IEnumerable<Produto> Produtos, int TotalCount)> GetProdutosPaginadosAsync(Guid empresaId, int page = 1, int pageSize = 20);
    }
}
