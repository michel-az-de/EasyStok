using EasyStock.Domain.Entities;

namespace EasyStock.Application.Ports.Output.Persistence
{
    public interface IProdutoVariacaoRepository : IBaseRepository<ProdutoVariacao>
    {
        Task<IEnumerable<ProdutoVariacao>> SearchAsync(Guid empresaId, string termo);
    }
}
