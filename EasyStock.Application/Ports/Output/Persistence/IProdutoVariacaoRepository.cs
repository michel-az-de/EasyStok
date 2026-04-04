using EasyStock.Domain.Entities;

namespace EasyStock.Application.Ports.Output.Persistence
{
    public interface IProdutoVariacaoRepository
    {
        Task<ProdutoVariacao?> GetByIdAsync(Guid id);
        Task<IEnumerable<ProdutoVariacao>> SearchAsync(Guid empresaId, string termo);
        Task InsertAsync(ProdutoVariacao variacao);
    }
}
