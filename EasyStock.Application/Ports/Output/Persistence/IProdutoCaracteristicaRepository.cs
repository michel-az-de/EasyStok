using EasyStock.Domain.Entities;

namespace EasyStock.Application.Ports.Output.Persistence
{
    public interface IProdutoCaracteristicaRepository
    {
        Task InsertAsync(ProdutoCaracteristica caracteristica);
    }
}
