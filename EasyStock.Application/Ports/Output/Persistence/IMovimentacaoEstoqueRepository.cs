using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;

namespace EasyStock.Application.Ports.Output.Persistence
{
    public sealed record KpisMovimentacao(int TotalUnidades, decimal ReceitaTotal, int TotalVendas, int TotalPerdas);

    public interface IMovimentacaoEstoqueRepository
    {
        Task InsertAsync(MovimentacaoEstoque movimentacao);
        Task InsertRangeAsync(IEnumerable<MovimentacaoEstoque> movimentacoes);
        Task<MovimentacaoEstoque?> GetByIdAsync(Guid id);
        Task UpdateAsync(MovimentacaoEstoque movimentacao);
        Task<(IEnumerable<MovimentacaoEstoque> Items, int TotalCount)> GetByEmpresaAsync(
            Guid empresaId,
            DateTime? de = null,
            DateTime? ate = null,
            TipoMovimentacaoEstoque? tipo = null,
            NaturezaMovimentacaoEstoque? natureza = null,
            int page = 1,
            int pageSize = 20);
        Task<KpisMovimentacao> GetKpisAsync(
            Guid empresaId,
            DateTime? de = null,
            DateTime? ate = null,
            TipoMovimentacaoEstoque? tipo = null,
            NaturezaMovimentacaoEstoque? natureza = null);
        Task<IEnumerable<MovimentacaoEstoque>> GetByProdutoAsync(Guid empresaId, Guid produtoId);
        Task<IEnumerable<MovimentacaoEstoque>> GetByItemEstoqueAsync(Guid itemEstoqueId);
        Task<decimal> GetTaxaSaidaDiariaAsync(Guid empresaId, Guid? produtoId, DateTime de, DateTime ate);
        Task<IReadOnlyDictionary<Guid, decimal>> GetTaxaSaidaDiariaPorProdutoAsync(Guid empresaId, IEnumerable<Guid> produtoIds, DateTime de, DateTime ate);
        Task<IEnumerable<(int Ano, int Mes, int TotalSaidas, decimal ValorTotal)>> GetAgregacaoMensalAsync(Guid empresaId, Guid produtoId, int meses = 12);
        Task<IEnumerable<MovimentacaoEstoque>> SearchAsync(Guid empresaId, string termo, int maxResults = 20);
    }
}
