using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Defaults;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Infra.Postgre.Repositories
{
    public sealed class ItemEstoqueRepository(EasyStockDbContext dbContext)
        : IItemEstoqueRepository
    {
        private const decimal FallbackMargemPrecoSugerido = OperacionalDefaults.FallbackMargemPrecoSugerido;
        public Task<ItemEstoque?> GetByIdAsync(Guid id) =>
            dbContext.ItensEstoque.FirstOrDefaultAsync(i => i.Id == id);

        public Task<ItemEstoque?> GetByIdAsync(Guid empresaId, Guid id) =>
            dbContext.ItensEstoque
                .AsNoTracking()
                .FirstOrDefaultAsync(i => i.EmpresaId == empresaId && i.Id == id);

        /// <summary>
        /// FOR UPDATE garante que o registro fique locked até o final da
        /// transação. Usar quando o caller vai modificar (ex: saída via
        /// ItemEstoqueId direto). Sem isso, dois usuários podem ler 5
        /// unidades, ambos validar, e ambos descontar — saldo negativo.
        /// </summary>
        public async Task<ItemEstoque?> GetByIdComLockAsync(Guid empresaId, Guid id)
        {
            // SELECT *, xmin: itens_estoque mapeia xmin (system column) como token de
            // concorrencia; FirstOrDefaultAsync compoe o FromSqlRaw em subquery e o
            // SELECT * nao expoe xmin -> EF gera e.xmin e estoura 42703.
            var sql = "SELECT *, xmin FROM itens_estoque WHERE \"EmpresaId\" = {0} AND \"Id\" = {1} FOR UPDATE";
            // IgnoreQueryFilters: a SQL raw JA filtra por "EmpresaId" = {0}, entao o
            // isolamento de tenant esta preservado pelo proprio SQL. O global query
            // filter do EF (EmpresaId == CurrentTenantId) so seria redundante aqui,
            // mas envolve a query em subselect ("SELECT * FROM (raw) sub WHERE ...")
            // — defesa-em-profundidade para nao perder ORDER BY/FOR UPDATE semantics
            // se a query evoluir. Cross-tenant continua bloqueado pelo WHERE literal.
            return await dbContext.ItensEstoque
                .FromSqlRaw(sql, empresaId, id)
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync();
        }

        public async Task<IEnumerable<ItemEstoque>> SearchAsync(Guid empresaId, string termo, int maxResults = 100)
        {
            termo = termo.Trim();
            if (string.IsNullOrWhiteSpace(termo)) return [];

            var pattern = $"%{termo}%";

            return await dbContext.ItensEstoque
                .AsNoTracking()
                .Where(i => i.EmpresaId == empresaId &&
                    ((i.CodigoInterno != null && EF.Functions.ILike(i.CodigoInterno, pattern)) ||
                     (i.CodigoMarketplace != null && EF.Functions.ILike(i.CodigoMarketplace, pattern)) ||
                     (i.ChavePesquisa != null && EF.Functions.ILike(i.ChavePesquisa, pattern)) ||
                     (i.VariacaoDescricao != null && EF.Functions.ILike(i.VariacaoDescricao, pattern)) ||
                     (i.Cor != null && EF.Functions.ILike(i.Cor, pattern)) ||
                     (i.Tamanho != null && EF.Functions.ILike(i.Tamanho, pattern)) ||
                     (i.DescricaoAnuncio != null && EF.Functions.ILike(i.DescricaoAnuncio, pattern))))
                .OrderBy(i => i.ChavePesquisa)
                .ThenBy(i => i.Id)
                .Take(maxResults)
                .ToListAsync();
        }

        public async Task<(IEnumerable<ItemEstoque> Items, int TotalCount)> GetEstoqueBaixoAsync(Guid empresaId, int limite, int page = 1, int pageSize = 20, Guid? lojaId = null)
        {
            var query = dbContext.ItensEstoque
                .AsNoTracking()
                .Where(i => i.EmpresaId == empresaId && (int)i.QuantidadeAtual <= limite);

            if (lojaId.HasValue)
                query = query.Where(i => i.LojaId == lojaId.Value);

            var totalCount = await query.CountAsync();
            var items = await query
                .OrderBy(i => (int)i.QuantidadeAtual)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return (items, totalCount);
        }

        public async Task<(IEnumerable<ItemEstoque> Items, int TotalCount)> GetProximoVencimentoAsync(Guid empresaId, int dias, int page = 1, int pageSize = 20, Guid? lojaId = null)
        {
            var cutoff = DateTime.UtcNow.AddDays(dias);
            var query = dbContext.ItensEstoque
                .AsNoTracking()
                .Where(i => i.EmpresaId == empresaId && i.ValidadeEm != null && (DateTime?)i.ValidadeEm <= cutoff);

            if (lojaId.HasValue)
                query = query.Where(i => i.LojaId == lojaId.Value);

            var totalCount = await query.CountAsync();
            var items = await query
                .OrderBy(i => (DateTime?)i.ValidadeEm)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return (items, totalCount);
        }

        public async Task<(IEnumerable<ItemEstoque> Items, int TotalCount)> GetItensParadosAsync(Guid empresaId, int diasSemMovimento, int page = 1, int pageSize = 20, Guid? lojaId = null)
        {
            var cutoff = DateTime.UtcNow.AddDays(-diasSemMovimento);
            var query = dbContext.ItensEstoque
                .AsNoTracking()
                .Where(i => i.EmpresaId == empresaId &&
                    (i.UltimaMovimentacaoEm == null || i.UltimaMovimentacaoEm < cutoff));

            if (lojaId.HasValue)
                query = query.Where(i => i.LojaId == lojaId.Value);

            var totalCount = await query.CountAsync();
            var items = await query
                .OrderByDescending(i => i.UltimaMovimentacaoEm ?? DateTime.MinValue)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return (items, totalCount);
        }

        public async Task<(IEnumerable<ItemEstoque> Items, int TotalCount)> GetSugestaoReposicaoAsync(Guid empresaId, int limiteQuantidade = 5, int page = 1, int pageSize = 20, Guid? lojaId = null)
        {
            var query = dbContext.ItensEstoque
                .AsNoTracking()
                .Where(i => i.EmpresaId == empresaId && (int)i.QuantidadeAtual < limiteQuantidade);

            if (lojaId.HasValue)
                query = query.Where(i => i.LojaId == lojaId.Value);

            var totalCount = await query.CountAsync();
            var items = await query
                .OrderBy(i => (int)i.QuantidadeAtual)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return (items, totalCount);
        }

        public async Task<(IEnumerable<ItemEstoque> Items, int TotalCount)> GetItensEstoquePaginadosAsync(Guid empresaId, int page = 1, int pageSize = 20, string? status = null, Guid? categoriaId = null)
        {
            var query = dbContext.ItensEstoque
                .AsNoTracking()
                .Where(i => i.EmpresaId == empresaId);

            var statusEnum = NormalizarStatusFiltro(status);
            if (statusEnum.HasValue)
                query = query.Where(i => i.Status == statusEnum.Value);

            if (categoriaId.HasValue)
                query = query.Where(i => i.Produto != null && i.Produto.CategoriaId == categoriaId.Value);

            var totalCount = await query.CountAsync();
            var items = await query
                .Include(i => i.Produto)
                .Include(i => i.ProdutoVariacao)
                .OrderByDescending(i => i.EntradaEm)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return (items, totalCount);
        }

        public async Task<(int Cadastrados, int ComSaldo)> GetContadoresEstoqueAsync(Guid empresaId, string? status = null, Guid? categoriaId = null)
        {
            var query = dbContext.ItensEstoque
                .AsNoTracking()
                .Where(i => i.EmpresaId == empresaId);

            var statusEnum = NormalizarStatusFiltro(status);
            if (statusEnum.HasValue)
                query = query.Where(i => i.Status == statusEnum.Value);

            if (categoriaId.HasValue)
                query = query.Where(i => i.Produto != null && i.Produto.CategoriaId == categoriaId.Value);

            var cadastrados = await query.CountAsync();
            var comSaldo = await query.CountAsync(i => (int)i.QuantidadeAtual > 0);
            return (cadastrados, comSaldo);
        }

        public async Task<(int QuantidadeEmEstoque, decimal ValorTotalEstoque, decimal TicketMedioSugerido)> GetResumoEstoqueAsync(Guid empresaId)
        {
            var resumo = await dbContext.ItensEstoque
                .AsNoTracking()
                .Where(i => i.EmpresaId == empresaId && i.Status != StatusItemEstoque.Vencido)
                .Select(i => new
                {
                    Quantidade = (int)i.QuantidadeAtual,
                    ValorTotal = ((decimal?)i.PrecoVendaSugerido ?? (decimal)i.CustoUnitario * FallbackMargemPrecoSugerido) * (int)i.QuantidadeAtual,
                    PrecoReferencia = (decimal?)i.PrecoVendaSugerido
                        ?? (decimal)i.CustoUnitario * FallbackMargemPrecoSugerido
                })
                .ToListAsync();

            if (resumo.Count == 0)
                return (0, 0m, 0m);

            return (
                resumo.Sum(i => i.Quantidade),
                resumo.Sum(i => i.ValorTotal),
                resumo.Average(i => i.PrecoReferencia));
        }

        public async Task<IReadOnlyCollection<ItemEstoque>> GetByProdutoAsync(Guid empresaId, Guid produtoId) =>
            await dbContext.ItensEstoque
                .AsNoTracking()
                .Include(i => i.Produto)
                .Include(i => i.ProdutoVariacao)
                .Where(i => i.EmpresaId == empresaId && i.ProdutoId == produtoId)
                .OrderByDescending(i => i.EntradaEm)
                .ToListAsync();

        public async Task<IReadOnlyDictionary<Guid, IReadOnlyCollection<ItemEstoque>>> GetByProdutosAsync(
            Guid empresaId, IEnumerable<Guid> produtoIds, Guid? lojaId, CancellationToken ct = default)
        {
            var ids = produtoIds.Distinct().ToList();
            if (ids.Count == 0)
                return new Dictionary<Guid, IReadOnlyCollection<ItemEstoque>>();

            var query = dbContext.ItensEstoque
                .AsNoTracking()
                .Where(i => i.EmpresaId == empresaId && ids.Contains(i.ProdutoId));

            if (lojaId.HasValue)
                query = query.Where(i => i.LojaId == lojaId.Value);

            var todos = await query
                .OrderByDescending(i => i.EntradaEm)
                .ToListAsync(ct);

            return todos
                .GroupBy(i => i.ProdutoId)
                .ToDictionary(
                    g => g.Key,
                    g => (IReadOnlyCollection<ItemEstoque>)g.ToList());
        }

        public async Task<IReadOnlyCollection<ItemEstoque>> GetLotesDisponiveisParaSaidaAsync(Guid empresaId, Guid produtoId, Guid? produtoVariacaoId, bool fefo = true)
        {
            // FOR UPDATE serializa concorrentes — lock adquirido no raw SQL DENTRO da
            // transacao do caller (DbContext.Database.CurrentTransaction). Evita estoque
            // negativo + perda de update sob carga.
            //
            // FEFO/FIFO determinismo: usamos SqlQueryRaw<Guid> pra extrair apenas o ID
            // ordenado pelo raw (Postgres respeita ORDER BY quando a query retorna
            // primitivos). Depois carregamos as entities tracked por ID e reordenamos
            // in-memory na ordem dos IDs (que veio do raw, ja correto).
            //
            // Por que NAO usar dbContext.ItensEstoque.FromSqlRaw direto: EF compoe o raw
            // como subquery ("SELECT * FROM (raw com ORDER BY...) sub") para alimentar
            // o ChangeTracker, e o outer SELECT em Postgres NAO preserva o ORDER BY
            // interno. .AsNoTracking() + .IgnoreQueryFilters() nao impediram o wrap em
            // testes — provedor parece sempre envolver entity sets.
            //
            // FOR UPDATE no raw com SqlQueryRaw<Guid>: o lock e adquirido nas rows
            // selecionadas (lock-level row, escopo da transacao). O reload subsequente
            // via dbContext.ItensEstoque.Where(...) re-le as mesmas rows JA travadas —
            // outros concorrentes esperam o commit. Semantica de serializacao preservada.

            var variacaoFilter = produtoVariacaoId.HasValue
                ? "AND \"ProdutoVariacaoId\" = {2}"
                : "AND \"ProdutoVariacaoId\" IS NULL";

            var orderBy = fefo
                ? "\"ValidadeEm\" NULLS LAST, \"EntradaEm\", \"CriadoEm\""
                : "\"EntradaEm\", \"CriadoEm\"";

            var sqlIds = $@"
                    SELECT ""Id"" AS ""Value"" FROM itens_estoque
                    WHERE ""EmpresaId"" = {{0}}
                      AND ""ProdutoId"" = {{1}}
                      AND ""QuantidadeAtual"" > 0
                      {variacaoFilter}
                    ORDER BY {orderBy}
                    FOR UPDATE";

            var idsQuery = produtoVariacaoId.HasValue
                ? dbContext.Database.SqlQueryRaw<Guid>(sqlIds, empresaId, produtoId, produtoVariacaoId.Value)
                : dbContext.Database.SqlQueryRaw<Guid>(sqlIds, empresaId, produtoId);

            var idsOrdenados = await idsQuery.ToListAsync();
            if (idsOrdenados.Count == 0) return Array.Empty<ItemEstoque>();

            // Carrega entities tracked (sem ordem garantida do banco — reorderamos abaixo).
            // IgnoreQueryFilters: raw acima ja filtrou por EmpresaId, e RLS (ADR-0010)
            // protege em camada 2. Cross-tenant impossivel.
            var entities = await dbContext.ItensEstoque
                .IgnoreQueryFilters()
                .Where(i => idsOrdenados.Contains(i.Id))
                .ToListAsync();

            // Reordena pela lista de IDs (que veio do raw com ORDER BY correto).
            var byId = entities.ToDictionary(i => i.Id);
            return idsOrdenados.Select(id => byId[id]).ToList();
        }

        public Task<bool> ExisteEstoqueDoProdutoAsync(Guid empresaId, Guid produtoId) =>
            dbContext.ItensEstoque
                .AsNoTracking()
                .AnyAsync(i => i.EmpresaId == empresaId && i.ProdutoId == produtoId && (int)i.QuantidadeAtual > 0);

        public Task<bool> ExisteEstoqueDaVariacaoAsync(Guid empresaId, Guid produtoId, Guid variacaoId) =>
            dbContext.ItensEstoque
                .AsNoTracking()
                .AnyAsync(i => i.EmpresaId == empresaId && i.ProdutoId == produtoId && i.ProdutoVariacaoId == variacaoId && (int)i.QuantidadeAtual > 0);

        public Task<ItemEstoque?> GetItemComProdutoAsync(Guid empresaId, Guid id) =>
            dbContext.ItensEstoque
                .AsNoTracking()
                .Include(i => i.Produto)
                .Include(i => i.ProdutoVariacao)
                .FirstOrDefaultAsync(i => i.EmpresaId == empresaId && i.Id == id);

        public Task InsertAsync(ItemEstoque itemEstoque) =>
            dbContext.ItensEstoque.AddAsync(itemEstoque).AsTask();

        public Task UpdateAsync(ItemEstoque itemEstoque)
        {
            dbContext.ItensEstoque.Update(itemEstoque);
            return Task.CompletedTask;
        }

        public Task UpdateRangeAsync(IEnumerable<ItemEstoque> itensEstoque)
        {
            dbContext.ItensEstoque.UpdateRange(itensEstoque);
            return Task.CompletedTask;
        }

        // Mapeia o valor PT-BR do querystring (vindo do frontend / deep-links do
        // Dashboard tipo /estoque?status=critico) para o enum em inglês. Antes,
        // Enum.TryParse falhava silenciosamente porque "critico" ≠ "Critical" e o
        // filtro era ignorado — a lista voltava completa, e a pill ativa parecia
        // não responder. Aceita variantes com e sem acento. Retorna null se o
        // valor é vazio ou não reconhecido (= "sem filtro de status").
        private static StatusItemEstoque? NormalizarStatusFiltro(string? status)
        {
            if (string.IsNullOrWhiteSpace(status))
                return null;

            return status.Trim().ToLowerInvariant() switch
            {
                "ok" => StatusItemEstoque.Ok,
                "warn" or "atencao" or "atenção" => StatusItemEstoque.Warn,
                "critical" or "critico" or "crítico" => StatusItemEstoque.Critical,
                "esgotado" => StatusItemEstoque.Esgotado,
                "slow" or "parado" => StatusItemEstoque.Slow,
                "vencido" => StatusItemEstoque.Vencido,
                "descartado" => StatusItemEstoque.Descartado,
                "bloqueado" => StatusItemEstoque.Bloqueado,
                _ => null
            };
        }
    }
}
