using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Entities;
using EasyStock.Infra.Postgre.Configuration;
using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace EasyStock.Infra.Postgre.Repositories
{
    // DTO intermediário para serialização de cache (value tuples não são suportados pelo System.Text.Json)
    internal sealed record PaginacaoCacheEntry(List<Produto> Produtos, int TotalCount);

    public sealed class ProdutoRepository(
        EasyStockDbContext dbContext,
        IDistributedCache? cache = null,
        IOptions<CacheOptions>? cacheOptions = null)
        : IProdutoRepository
    {
        private CacheOptions Options => cacheOptions?.Value ?? new CacheOptions();

        public Task<Produto?> GetByIdAsync(Guid id) =>
            dbContext.Produtos.FirstOrDefaultAsync(p => p.Id == id);

        public Task<Produto?> GetByIdAsync(Guid empresaId, Guid id) =>
            dbContext.Produtos
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.EmpresaId == empresaId && p.Id == id);

        public Task<Produto?> GetDetalheAsync(Guid empresaId, Guid id) =>
            dbContext.Produtos
                .AsNoTracking()
                .Include(p => p.Caracteristicas)
                .Include(p => p.Embalagens)
                .Include(p => p.Variacoes)
                .FirstOrDefaultAsync(p => p.EmpresaId == empresaId && p.Id == id);

        public Task<bool> ExistsSkuBaseAsync(Guid empresaId, string skuBase, Guid? ignoreProdutoId = null)
        {
            skuBase = skuBase.Trim();

            return dbContext.Produtos
                .AsNoTracking()
                .Where(p => p.EmpresaId == empresaId && p.SkuBase != null && p.SkuBase.Value == skuBase)
                .Where(p => !ignoreProdutoId.HasValue || p.Id != ignoreProdutoId.Value)
                .AnyAsync();
        }

        public async Task<IEnumerable<Produto>> SearchAsync(Guid empresaId, string termo, int maxResults = 100)
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
                     (p.SkuBase != null && EF.Functions.ILike(p.SkuBase.Value, pattern)) ||
                     (p.CodigoBarras != null && EF.Functions.ILike(p.CodigoBarras, pattern))))
                .Take(maxResults)
                .ToListAsync();
        }

        public async Task<(IEnumerable<Produto> Produtos, int TotalCount)> GetProdutosPaginadosAsync(
            Guid empresaId, int page = 1, int pageSize = 20, string? sort = "nome", string? order = "asc")
        {
            var versao = cache is not null ? await cache.GetStringAsync(VersaoKey(empresaId)) ?? "0" : "0";
            var cacheKey = $"produtos_paginados_{empresaId}_v{versao}_{page}_{pageSize}_alteradoem_desc";

            if (cache is not null)
            {
                var cachedData = await cache.GetStringAsync(cacheKey);
                if (!string.IsNullOrEmpty(cachedData))
                {
                    var entry = JsonSerializer.Deserialize<PaginacaoCacheEntry>(cachedData);
                    if (entry is not null)
                        return (entry.Produtos, entry.TotalCount);
                }
            }

            var query = dbContext.Produtos
                .AsNoTracking()
                .Where(p => p.EmpresaId == empresaId);

            var totalCount = await query.CountAsync();

            query = query
                .OrderByDescending(p => p.AlteradoEm)
                .ThenByDescending(p => p.CriadoEm)
                .ThenByDescending(p => p.Id);

            var produtos = await query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            if (cache is not null)
            {
                var entry = new PaginacaoCacheEntry(produtos, totalCount);
                var serialized = JsonSerializer.Serialize(entry);
                await cache.SetStringAsync(cacheKey, serialized, new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = Options.ProdutosPaginadosDuration
                });
            }

            return (produtos, totalCount);
        }

        public async Task InsertAsync(Produto produto)
        {
            await dbContext.Produtos.AddAsync(produto);
            await InvalidarCacheAsync(produto.EmpresaId);
        }

        public async Task UpdateAsync(Produto produto)
        {
            dbContext.Produtos.Update(produto);
            await InvalidarCacheAsync(produto.EmpresaId);
        }

        private async Task InvalidarCacheAsync(Guid empresaId)
        {
            if (cache is null) return;
            var novaVersao = DateTime.UtcNow.Ticks.ToString();
            await cache.SetStringAsync(VersaoKey(empresaId), novaVersao, new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = Options.ProdutosVersaoDuration
            });
        }

        private static string VersaoKey(Guid empresaId) => $"produtos_versao_{empresaId}";
    }
}
