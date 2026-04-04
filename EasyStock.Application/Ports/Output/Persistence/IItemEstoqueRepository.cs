using EasyStock.Domain.Entities;

namespace EasyStock.Application.Ports.Output.Persistence
{
    public interface IItemEstoqueRepository : IBaseRepository<ItemEstoque>
    {
        Task<IEnumerable<ItemEstoque>> SearchAsync(Guid empresaId, string termo);
        Task<(IEnumerable<ItemEstoque> Items, int TotalCount)> GetEstoqueBaixoAsync(Guid empresaId, int limite, int page = 1, int pageSize = 20);
        Task<(IEnumerable<ItemEstoque> Items, int TotalCount)> GetProximoVencimentoAsync(Guid empresaId, int dias, int page = 1, int pageSize = 20);
        Task<(IEnumerable<ItemEstoque> Items, int TotalCount)> GetItensParadosAsync(Guid empresaId, int diasSemMovimento, int page = 1, int pageSize = 20);
        Task<(IEnumerable<ItemEstoque> Items, int TotalCount)> GetSugestaoReposicaoAsync(Guid empresaId, int page = 1, int pageSize = 20);
        Task<(IEnumerable<ItemEstoque> Items, int TotalCount)> GetItensEstoquePaginadosAsync(Guid empresaId, int page = 1, int pageSize = 20);
    }
}
