using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;

namespace EasyStock.Application.Ports.Output.Persistence
{
    public interface IMovimentacaoEstoqueRepository
    {
        Task InsertAsync(MovimentacaoEstoque movimentacao);
        Task InsertRangeAsync(IEnumerable<MovimentacaoEstoque> movimentacoes);
        Task<(IEnumerable<MovimentacaoEstoque> Items, int TotalCount)> GetByEmpresaAsync(
            Guid empresaId,
            DateTime? de = null,
            DateTime? ate = null,
            TipoMovimentacaoEstoque? tipo = null,
            int page = 1,
            int pageSize = 20);
        Task<IEnumerable<MovimentacaoEstoque>> GetByProdutoAsync(Guid empresaId, Guid produtoId);
        Task<IEnumerable<MovimentacaoEstoque>> GetByItemEstoqueAsync(Guid itemEstoqueId);
        Task<decimal> GetTaxaSaidaDiariaAsync(Guid empresaId, Guid? produtoId, DateTime de, DateTime ate);
        Task<IReadOnlyDictionary<Guid, decimal>> GetTaxaSaidaDiariaPorProdutoAsync(Guid empresaId, IEnumerable<Guid> produtoIds, DateTime de, DateTime ate);
        Task<IEnumerable<(int Ano, int Mes, int TotalSaidas, decimal ValorTotal)>> GetAgregacaoMensalAsync(Guid empresaId, Guid produtoId, int meses = 12);
    }
}
