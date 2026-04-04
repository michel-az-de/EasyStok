using EasyStock.Domain.Entities;

namespace EasyStock.Application.Ports.Output.Persistence
{
    public interface IItemEstoqueRepository
    {
        Task<ItemEstoque?> GetByIdAsync(Guid id);
        Task<ItemEstoque?> GetByIdAsync(Guid empresaId, Guid id);
        Task<IEnumerable<ItemEstoque>> SearchAsync(Guid empresaId, string termo);
        Task<(IEnumerable<ItemEstoque> Items, int TotalCount)> GetEstoqueBaixoAsync(Guid empresaId, int limite, int page = 1, int pageSize = 20);
        Task<(IEnumerable<ItemEstoque> Items, int TotalCount)> GetProximoVencimentoAsync(Guid empresaId, int dias, int page = 1, int pageSize = 20);
        Task<(IEnumerable<ItemEstoque> Items, int TotalCount)> GetItensParadosAsync(Guid empresaId, int diasSemMovimento, int page = 1, int pageSize = 20);
        Task<(IEnumerable<ItemEstoque> Items, int TotalCount)> GetSugestaoReposicaoAsync(Guid empresaId, int limiteQuantidade = 5, int page = 1, int pageSize = 20);
        Task<(IEnumerable<ItemEstoque> Items, int TotalCount)> GetItensEstoquePaginadosAsync(Guid empresaId, int page = 1, int pageSize = 20);
        Task<(int QuantidadeEmEstoque, decimal ValorTotalEstoque, decimal TicketMedioSugerido)> GetResumoEstoqueAsync(Guid empresaId);
        Task<ItemEstoque?> GetItemComProdutoAsync(Guid empresaId, Guid id);
        Task InsertAsync(ItemEstoque itemEstoque);
        Task UpdateAsync(ItemEstoque itemEstoque);
    }
}
