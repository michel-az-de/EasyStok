using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Entities;
using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace EasyStock.Infra.Postgre.Repositories
{
    // Proposed PostgreSQL index for performance:
    // CREATE INDEX idx_produtos_empresa_nome ON produtos (empresaid, nome);

    public sealed class ProdutoRepository(EasyStockDbContext dbContext, IMemoryCache? cache = null)
        : IProdutoRepository
    {
        private readonly IMemoryCache _cache = cache ?? new MemoryCache(new MemoryCacheOptions());
        private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

        public Task<Produto?> GetByIdAsync(Guid id) =>
            dbContext.Produtos.FirstOrDefaultAsync(p => p.Id == id);

        public Task<Produto?> GetByIdAsync(Guid empresaId, Guid id) =>
            dbContext.Produtos
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.EmpresaId == empresaId && p.Id == id);

        public async Task<IEnumerable<Produto>> SearchAsync(Guid empresaId, string termo)
        {
            termo = termo.Trim();
            if (string.IsNullOrWhiteSpace(termo)) return [];

            var pattern = $"%{termo}%";

            return await dbContext.Produtos
                .AsNoTracking()
                .Where(p => p.EmpresaId == empresaId &&
                    (EF.Functions.ILike(p.Nome, pattern) ||
                     (p.Marca != null && EF.Functions.ILike(p.Marca, pattern)) ||
                     (p.DescricaoBase != null && EF.Functions.ILike(p.DescricaoBase, pattern)) ||
                     EF.Functions.ILike(EF.Property<string?>(p, nameof(Produto.SkuBase))!, pattern) ||
                     (p.CodigoBarras != null && EF.Functions.ILike(p.CodigoBarras, pattern))))
                .ToListAsync();
        }

        public async Task<(IEnumerable<Produto> Produtos, int TotalCount)> GetProdutosPaginadosAsync(Guid empresaId, int page = 1, int pageSize = 20)
        {
            var cacheKey = $"produtos_paginados_{empresaId}_{page}_{pageSize}";

            if (_cache.TryGetValue(cacheKey, out (IEnumerable<Produto> Produtos, int TotalCount) cachedResult))
            {
                return cachedResult;
            }

            var query = dbContext.Produtos
                .AsNoTracking()
                .Where(p => p.EmpresaId == empresaId);

            var totalCount = await query.CountAsync();
            var produtos = await query
                .OrderBy(p => p.Nome)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            var result = (produtos, totalCount);
            _cache.Set(cacheKey, result, CacheDuration);

            return result;
        }

        public Task InsertAsync(Produto produto) =>
            dbContext.Produtos.AddAsync(produto).AsTask();

        public Task UpdateAsync(Produto produto)
        {
            dbContext.Produtos.Update(produto);
            return Task.CompletedTask;
        }
    }
}
