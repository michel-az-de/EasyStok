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
        Task<(IEnumerable<ItemEstoque> Items, int TotalCount)> GetProximoVencimentoAsync(Guid empresaId, int dias, int page = 1, int pageSize = 20, Guid? lojaId = null);
        Task<(IEnumerable<ItemEstoque> Items, int TotalCount)> GetItensParadosAsync(Guid empresaId, int diasSemMovimento, int page = 1, int pageSize = 20, Guid? lojaId = null);
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
        /// <summary>
        /// Batch (#1171): saldo disponível somado por produto numa única query
        /// (<c>WHERE EmpresaId = @e AND ProdutoId IN (...)</c>). Conta só lote com
        /// <c>QuantidadeAtual &gt; 0</c> e não vencido (mesmo critério de #983 para saída).
        /// Produto sem saldo não aparece no dicionário (consumer trata ausência como 0).
        /// Não aplica filtro global de tenant: o caller é o cardápio público anônimo, e o
        /// isolamento vem do <paramref name="empresaId"/> explícito no WHERE.
        /// </summary>
        Task<IReadOnlyDictionary<Guid, decimal>> GetSaldoDisponivelPorProdutosAsync(Guid empresaId, IReadOnlyCollection<Guid> produtoIds, CancellationToken ct = default);
        /// <param name="fefo">true = FEFO (saída pelo lote com validade mais próxima); false = FIFO (saída pela entrada mais antiga).</param>
        /// <param name="incluirVencidos">
        /// false (padrão) = exclui lotes com <c>ValidadeEm</c> vencido (#983) — evita que uma
        /// venda comum derrube a transação inteira por causa de um lote vencido no meio do FEFO.
        /// true = inclui vencidos; usar apenas quando o caller vai dar baixa neles (natureza que
        /// satisfaz <see cref="Domain.Enums.NaturezaMovimentacaoEstoqueExtensions.PermiteBaixaDeLoteVencido"/>).
        /// </param>
        Task<IReadOnlyCollection<ItemEstoque>> GetLotesDisponiveisParaSaidaAsync(Guid empresaId, Guid produtoId, Guid? produtoVariacaoId, bool fefo = true, bool incluirVencidos = false);
        /// <summary>
        /// Lotes do produto (opcionalmente da loja) para o ajuste rápido de saldo (S22): RASTREADOS,
        /// inclui saldo 0 e descoberto, exclui Descartado. Ordenados do mais antigo para o mais novo.
        /// </summary>
        Task<IReadOnlyList<ItemEstoque>> GetLotesParaAjusteAsync(Guid empresaId, Guid produtoId, Guid? lojaId, CancellationToken ct = default);
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

        /// <summary>
        /// M2.6 (#1511): lotes com saldo e validade até <paramref name="validadeAteUtc"/>, com o produto.
        /// O chamador confere o vencimento no dia operacional (a validade é data civil).
        /// </summary>
        Task<IReadOnlyList<ItemEstoque>> GetComSaldoEValidadeAteAsync(Guid empresaId, DateTime validadeAteUtc, CancellationToken ct = default);
    }
}
