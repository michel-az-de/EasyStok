using EasyStock.Domain.Entities;

namespace EasyStock.Application.Ports.Output.Persistence
{
    public interface IProdutoEmbalagemRepository
    {
        Task InsertAsync(ProdutoEmbalagem embalagem);
    }
}
