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
        Task<(IEnumerable<ItemEstoque> Items, int TotalCount)> GetItensEstoquePaginadosAsync(Guid empresaId, int page = 1, int pageSize = 20, string? status = null, Guid? categoriaId = null, string? termo = null);

        /// <summary>
        /// Contadores rapidos para o cabecalho de /estoque, batendo com o mesmo
        /// universo de GetItensEstoquePaginadosAsync mas separando "lotes
        /// cadastrados" (tudo) de "lotes com saldo" (qty > 0). Permite a UI
        /// exibir os dois numeros sem confusao com o KPI "Unidades em estoque"
        /// do dashboard (que conta unidades, nao linhas).
        /// </summary>
        Task<(int Cadastrados, int ComSaldo)> GetContadoresEstoqueAsync(Guid empresaId, string? status = null, Guid? categoriaId = null);

        Task<(int QuantidadeEmEstoque, decimal ValorTotalEstoque, decimal TicketMedioSugerido)> GetResumoEstoqueAsync(Guid empresaId);
        Task<IReadOnlyCollection<ItemEstoque>> GetByProdutoAsync(Guid empresaId, Guid produtoId);

        /// <summary>
        /// Batch: traz lotes de varios produtos numa unica query (`WHERE ProdutoId IN (...)`).
        /// Usado pela calculadora de producao pra evitar N round trips em receitas com varios insumos.
        /// Filtra por loja se <paramref name="lojaId"/> informado.
        /// Retorna dicionario produtoId -> lotes; produto sem estoque nao aparece (consumer usa TryGetValue + default vazio).
        /// </summary>
        Task<IReadOnlyDictionary<Guid, IReadOnlyCollection<ItemEstoque>>> GetByProdutosAsync(Guid empresaId, IEnumerable<Guid> produtoIds, Guid? lojaId, CancellationToken ct = default);
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
