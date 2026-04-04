using EasyStock.Domain.Entities;

namespace EasyStock.Application.Ports.Output.Persistence
{
    public interface IItemEstoqueRepository : IBaseRepository<ItemEstoque>
    {
        Task<IEnumerable<ItemEstoque>> SearchAsync(Guid empresaId, string termo);
    }
}
