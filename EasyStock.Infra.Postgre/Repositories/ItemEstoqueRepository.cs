using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Infra.Postgre.Repositories
{
    public sealed class ItemEstoqueRepository(EasyStockDbContext dbContext)
        : IItemEstoqueRepository
    {
        private const decimal FallbackMargemPrecoSugerido = 1.3m;
        public Task<ItemEstoque?> GetByIdAsync(Guid id) =>
            dbContext.ItensEstoque.FirstOrDefaultAsync(i => i.Id == id);

        public Task<ItemEstoque?> GetByIdAsync(Guid empresaId, Guid id) =>
            dbContext.ItensEstoque
                .AsNoTracking()
                .FirstOrDefaultAsync(i => i.EmpresaId == empresaId && i.Id == id);

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

        public async Task<(IEnumerable<ItemEstoque> Items, int TotalCount)> GetItensEstoquePaginadosAsync(Guid empresaId, int page = 1, int pageSize = 20)
        {
            var query = dbContext.ItensEstoque
                .AsNoTracking()
                .Where(i => i.EmpresaId == empresaId);

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

        public async Task<IReadOnlyCollection<ItemEstoque>> GetLotesDisponiveisParaSaidaAsync(Guid empresaId, Guid produtoId, Guid? produtoVariacaoId)
        {
            // FOR UPDATE garante serialização: requests concorrentes aguardam o lock
            // antes de ler a quantidade, evitando estoque negativo.
            // Usa dois templates separados para deixar o filtro de variacao como
            // literal fixo e evitar interpolação de valor no SQL (avisos EF1002).
            var variacaoFilter = produtoVariacaoId.HasValue
                ? "AND \"ProdutoVariacaoId\" = {2}"
                : "AND \"ProdutoVariacaoId\" IS NULL";

            var sql = $@"
                    SELECT * FROM itens_estoque
                    WHERE ""EmpresaId"" = {{0}}
                      AND ""ProdutoId"" = {{1}}
                      AND ""QuantidadeAtual"" > 0
                      {variacaoFilter}
                    ORDER BY ""EntradaEm"", ""CriadoEm""
                    FOR UPDATE";

            var query = produtoVariacaoId.HasValue
                ? dbContext.ItensEstoque.FromSqlRaw(sql, empresaId, produtoId, produtoVariacaoId.Value)
                : dbContext.ItensEstoque.FromSqlRaw(sql, empresaId, produtoId);

            return await query
                .ToListAsync();
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
    }
}
