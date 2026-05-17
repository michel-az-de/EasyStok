using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Infra.Postgre.Repositories
{
    public sealed class MovimentacaoEstoqueRepository(EasyStockDbContext dbContext)
        : IMovimentacaoEstoqueRepository
    {
        public Task InsertAsync(MovimentacaoEstoque movimentacao) =>
            dbContext.MovimentacoesEstoque.AddAsync(movimentacao).AsTask();

        public Task InsertRangeAsync(IEnumerable<MovimentacaoEstoque> movimentacoes)
        {
            dbContext.MovimentacoesEstoque.AddRange(movimentacoes);
            return Task.CompletedTask;
        }

        public async Task<MovimentacaoEstoque?> GetByIdAsync(Guid id) =>
            await dbContext.MovimentacoesEstoque
                .Include(m => m.ItemEstoque)
                .FirstOrDefaultAsync(m => m.Id == id);

        public async Task<MovimentacaoEstoque?> GetByIdComLockAsync(Guid id) =>
            await dbContext.MovimentacoesEstoque
                .FromSqlRaw("SELECT * FROM movimentacoes_estoque WHERE \"Id\" = {0} FOR UPDATE", id)
                .FirstOrDefaultAsync();

        public Task UpdateAsync(MovimentacaoEstoque movimentacao)
        {
            dbContext.MovimentacoesEstoque.Update(movimentacao);
            return Task.CompletedTask;
        }

        public async Task<(IEnumerable<MovimentacaoEstoque> Items, int TotalCount)> GetByEmpresaAsync(
            Guid empresaId,
            DateTime? de = null,
            DateTime? ate = null,
            TipoMovimentacaoEstoque? tipo = null,
            NaturezaMovimentacaoEstoque? natureza = null,
            int page = 1,
            int pageSize = 20)
        {
            var query = BuildFilteredQuery(empresaId, de, ate, tipo, natureza);

            var totalCount = await query.CountAsync();
            var items = await query
                .Include(m => m.Produto)
                .Include(m => m.ProdutoVariacao)
                .OrderByDescending(m => m.DataMovimentacao)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return (items, totalCount);
        }

        public async Task<KpisMovimentacao> GetKpisAsync(
            Guid empresaId,
            DateTime? de = null,
            DateTime? ate = null,
            TipoMovimentacaoEstoque? tipo = null,
            NaturezaMovimentacaoEstoque? natureza = null)
        {
            var query = BuildFilteredQuery(empresaId, de, ate, tipo, natureza);

            // Projeção com campos primitivos para garantir tradução SQL pelo EF Core
            var aggregated = await query
                .Select(m => new
                {
                    Quantidade = (int)m.Quantidade,
                    ValorTotal = (decimal?)m.ValorTotal,
                    m.Natureza
                })
                .ToListAsync();

            var totalUnidades = aggregated.Sum(m => m.Quantidade);
            var receitaTotal = aggregated.Sum(m => m.ValorTotal ?? 0m);
            var totalVendas = aggregated.Count(m => m.Natureza == NaturezaMovimentacaoEstoque.Venda);
            var totalPerdas = aggregated.Count(m => m.Natureza == NaturezaMovimentacaoEstoque.Perda);

            return new KpisMovimentacao(totalUnidades, receitaTotal, totalVendas, totalPerdas);
        }

        private IQueryable<MovimentacaoEstoque> BuildFilteredQuery(
            Guid empresaId,
            DateTime? de,
            DateTime? ate,
            TipoMovimentacaoEstoque? tipo,
            NaturezaMovimentacaoEstoque? natureza)
        {
            var query = dbContext.MovimentacoesEstoque
                .AsNoTracking()
                .Where(m => m.EmpresaId == empresaId);

            if (de.HasValue)
                query = query.Where(m => m.DataMovimentacao >= DateTime.SpecifyKind(de.Value, DateTimeKind.Utc));
            if (ate.HasValue)
                query = query.Where(m => m.DataMovimentacao <= DateTime.SpecifyKind(ate.Value, DateTimeKind.Utc));
            if (tipo.HasValue)
                query = query.Where(m => m.Tipo == tipo.Value);
            if (natureza.HasValue)
                query = query.Where(m => m.Natureza == natureza.Value);

            return query;
        }

        public async Task<IEnumerable<MovimentacaoEstoque>> GetByItemEstoqueAsync(Guid empresaId, Guid itemEstoqueId) =>
            await dbContext.MovimentacoesEstoque
                .AsNoTracking()
                .Where(m => m.EmpresaId == empresaId && m.ItemEstoqueId == itemEstoqueId)
                .OrderByDescending(m => m.DataMovimentacao)
                .ToListAsync();

        public async Task<IEnumerable<MovimentacaoEstoque>> GetByProdutoAsync(Guid empresaId, Guid produtoId) =>
            await dbContext.MovimentacoesEstoque
                .AsNoTracking()
                .Where(m => m.EmpresaId == empresaId && m.ProdutoId == produtoId)
                .OrderByDescending(m => m.DataMovimentacao)
                .ToListAsync();

        public Task<bool> ExisteReferenciaAsync(
            Guid empresaId,
            Guid produtoId,
            string referenciaDocumento,
            NaturezaMovimentacaoEstoque natureza,
            CancellationToken ct = default) =>
            dbContext.MovimentacoesEstoque.AsNoTracking()
                .AnyAsync(m => m.EmpresaId == empresaId &&
                               m.ProdutoId == produtoId &&
                               m.DocumentoReferencia == referenciaDocumento &&
                               m.Natureza == natureza, ct);

        public async Task<decimal> GetTaxaSaidaDiariaAsync(Guid empresaId, Guid? produtoId, DateTime de, DateTime ate)
        {
            var query = dbContext.MovimentacoesEstoque
                .AsNoTracking()
                .Where(m => m.EmpresaId == empresaId &&
                            m.Tipo == TipoMovimentacaoEstoque.Saida &&
                            m.DataMovimentacao >= de &&
                            m.DataMovimentacao <= ate);

            if (produtoId.HasValue)
                query = query.Where(m => m.ProdutoId == produtoId.Value);

            var totalSaidas = await query.Select(m => (int)m.Quantidade).SumAsync();
            var dias = Math.Max(1, (ate - de).Days);
            return (decimal)totalSaidas / dias;
        }

        public async Task<IReadOnlyDictionary<Guid, decimal>> GetTaxaSaidaDiariaPorProdutoAsync(Guid empresaId, IEnumerable<Guid> produtoIds, DateTime de, DateTime ate)
        {
            var produtos = produtoIds.Distinct().ToArray();
            if (produtos.Length == 0)
                return new Dictionary<Guid, decimal>();

            var dias = Math.Max(1, (ate - de).Days);

            var totais = await dbContext.MovimentacoesEstoque
                .AsNoTracking()
                .Where(m => m.EmpresaId == empresaId &&
                            produtos.Contains(m.ProdutoId) &&
                            m.Tipo == TipoMovimentacaoEstoque.Saida &&
                            m.DataMovimentacao >= de &&
                            m.DataMovimentacao <= ate)
                .Select(m => new { m.ProdutoId, Quantidade = (int)m.Quantidade })
                .GroupBy(m => m.ProdutoId)
                .Select(g => new
                {
                    ProdutoId = g.Key,
                    TotalSaidas = g.Sum(m => m.Quantidade)
                })
                .ToListAsync();

            return totais.ToDictionary(x => x.ProdutoId, x => (decimal)x.TotalSaidas / dias);
        }

        public async Task<IEnumerable<(int Ano, int Mes, int TotalSaidas, decimal ValorTotal)>> GetAgregacaoMensalAsync(
            Guid empresaId, Guid produtoId, int meses = 12)
        {
            var de = DateTime.UtcNow.AddMonths(-meses);

            var resultado = await dbContext.MovimentacoesEstoque
                .AsNoTracking()
                .Where(m => m.EmpresaId == empresaId &&
                            m.ProdutoId == produtoId &&
                            m.Tipo == TipoMovimentacaoEstoque.Saida &&
                            m.DataMovimentacao >= de)
                .Select(m => new
                {
                    m.DataMovimentacao,
                    Quantidade = (int)m.Quantidade,
                    ValorTotal = (decimal?)m.ValorTotal ?? 0m
                })
                .GroupBy(m => new { m.DataMovimentacao.Year, m.DataMovimentacao.Month })
                .Select(g => new
                {
                    Ano = g.Key.Year,
                    Mes = g.Key.Month,
                    TotalSaidas = g.Sum(m => m.Quantidade),
                    ValorTotal = g.Sum(m => m.ValorTotal)
                })
                .OrderBy(x => x.Ano).ThenBy(x => x.Mes)
                .ToListAsync();

            return resultado.Select(x => (x.Ano, x.Mes, x.TotalSaidas, x.ValorTotal));
        }

        public async Task<IEnumerable<MovimentacaoEstoque>> SearchAsync(Guid empresaId, string termo, int maxResults = 20)
        {
            var pattern = $"%{termo.Trim()}%";
            return await dbContext.MovimentacoesEstoque
                .AsNoTracking()
                .Include(m => m.Produto)
                .Where(m => m.EmpresaId == empresaId &&
                    ((m.Descricao != null && EF.Functions.ILike(m.Descricao, pattern)) ||
                     (m.DocumentoReferencia != null && EF.Functions.ILike(m.DocumentoReferencia, pattern)) ||
                     (m.Produto != null && EF.Functions.ILike(m.Produto.Nome, pattern))))
                .OrderByDescending(m => m.DataMovimentacao)
                .Take(maxResults)
                .ToListAsync();
        }
    }
}
