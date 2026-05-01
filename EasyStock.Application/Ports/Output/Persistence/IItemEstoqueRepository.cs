using EasyStock.Domain.Entities;

namespace EasyStock.Application.Ports.Output.Persistence
{
    public interface IItemEstoqueRepository
    {
        Task<ItemEstoque?> GetByIdAsync(Guid id);
        Task<ItemEstoque?> GetByIdAsync(Guid empresaId, Guid id);

        /// <summary>
        /// Versão com lock pessimista (FOR UPDATE no Postgres) — usar quando o
        /// ItemEstoqueId vem direto do client (caminho "saída direta" sem FIFO),
        /// pra evitar saldo negativo em concorrência.
        /// </summary>
        Task<ItemEstoque?> GetByIdComLockAsync(Guid empresaId, Guid id);
        Task<IEnumerable<ItemEstoque>> SearchAsync(Guid empresaId, string termo, int maxResults = 100);
        Task<(IEnumerable<ItemEstoque> Items, int TotalCount)> GetEstoqueBaixoAsync(Guid empresaId, int limite, int page = 1, int pageSize = 20, Guid? lojaId = null);
        Task<(IEnumerable<ItemEstoque> Items, int TotalCount)> GetProximoVencimentoAsync(Guid empresaId, int dias, int page = 1, int pageSize = 20, Guid? lojaId = null);
        Task<(IEnumerable<ItemEstoque> Items, int TotalCount)> GetItensParadosAsync(Guid empresaId, int diasSemMovimento, int page = 1, int pageSize = 20, Guid? lojaId = null);
        Task<(IEnumerable<ItemEstoque> Items, int TotalCount)> GetSugestaoReposicaoAsync(Guid empresaId, int limiteQuantidade = 5, int page = 1, int pageSize = 20, Guid? lojaId = null);
        Task<(IEnumerable<ItemEstoque> Items, int TotalCount)> GetItensEstoquePaginadosAsync(Guid empresaId, int page = 1, int pageSize = 20, string? status = null);
        Task<(int QuantidadeEmEstoque, decimal ValorTotalEstoque, decimal TicketMedioSugerido)> GetResumoEstoqueAsync(Guid empresaId);
        Task<IReadOnlyCollection<ItemEstoque>> GetByProdutoAsync(Guid empresaId, Guid produtoId);
        /// <param name="fefo">true = FEFO (saída pelo lote com validade mais próxima); false = FIFO (saída pela entrada mais antiga).</param>
        Task<IReadOnlyCollection<ItemEstoque>> GetLotesDisponiveisParaSaidaAsync(Guid empresaId, Guid produtoId, Guid? produtoVariacaoId, bool fefo = true);
        Task<bool> ExisteEstoqueDoProdutoAsync(Guid empresaId, Guid produtoId);
        Task<bool> ExisteEstoqueDaVariacaoAsync(Guid empresaId, Guid produtoId, Guid variacaoId);
        Task<ItemEstoque?> GetItemComProdutoAsync(Guid empresaId, Guid id);
        Task InsertAsync(ItemEstoque itemEstoque);
        Task UpdateAsync(ItemEstoque itemEstoque);

        /// <summary>
        /// Atualiza um conjunto de itens de estoque em batch (ex.: lotes tocados
        /// por uma mesma saída FIFO). Implementações devem marcar todos os
        /// itens como Modified e evitar round-trips individuais.
        /// </summary>
        Task UpdateRangeAsync(IEnumerable<ItemEstoque> itensEstoque);
    }
}
