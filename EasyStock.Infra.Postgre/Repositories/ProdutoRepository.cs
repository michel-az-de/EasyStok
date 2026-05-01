using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Domain.ValueObjects;
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
            var sku = CodigoSku.From(skuBase.Trim());

            return dbContext.Produtos
                .AsNoTracking()
                .Where(p => p.EmpresaId == empresaId && p.SkuBase == sku)
                .Where(p => !ignoreProdutoId.HasValue || p.Id != ignoreProdutoId.Value)
                .AnyAsync();
        }

        public Task<bool> ExistsCodigoBarrasAsync(Guid empresaId, string codigoBarras, Guid? ignoreProdutoId = null)
        {
            var cb = codigoBarras.Trim();
            return dbContext.Produtos
                .AsNoTracking()
                .Where(p => p.EmpresaId == empresaId && p.CodigoBarras == cb)
                .Where(p => !ignoreProdutoId.HasValue || p.Id != ignoreProdutoId.Value)
                .AnyAsync();
        }

        public Task<bool> ExistsNomeAsync(Guid empresaId, string nome, Guid? ignoreProdutoId = null)
        {
            var n = nome.Trim();
            return dbContext.Produtos
                .AsNoTracking()
                .Where(p => p.EmpresaId == empresaId && p.Nome == n)
                .Where(p => !ignoreProdutoId.HasValue || p.Id != ignoreProdutoId.Value)
                .AnyAsync();
        }

        public async Task<IEnumerable<Produto>> SearchAsync(Guid empresaId, string termo, int maxResults = 20)
        {
            termo = termo.Trim();
            if (string.IsNullOrWhiteSpace(termo)) return [];

            var pattern = $"%{termo}%";

            // SKU usa Value Object com HasConversion — busca exata pelo VO
            CodigoSku? skuExato = null;
            try { skuExato = CodigoSku.From(termo); } catch (ArgumentException) { /* termo invalido para SKU */ }

            return await dbContext.Produtos
                .AsNoTracking()
                .Where(p => p.EmpresaId == empresaId &&
                    (EF.Functions.ILike(p.Nome, pattern) ||
                     (p.Marca != null && EF.Functions.ILike(p.Marca, pattern)) ||
                     (p.DescricaoBase != null && EF.Functions.ILike(p.DescricaoBase, pattern)) ||
                     (skuExato != null && p.SkuBase == skuExato) ||
                     (p.CodigoBarras != null && EF.Functions.ILike(p.CodigoBarras, pattern))))
                .OrderBy(p => p.Nome)
                .Take(maxResults)
                .ToListAsync();
        }

        public async Task<(IEnumerable<Produto> Produtos, int TotalCount)> GetProdutosPaginadosAsync(
            Guid empresaId,
            int page = 1,
            int pageSize = 20,
            string? sort = "criadoem",
            string? order = "desc",
            StatusProduto? status = null,
            bool semPreco = false,
            Guid? categoriaId = null)
        {
            var sortNorm  = (sort  ?? "criadoem").ToLowerInvariant();
            var orderNorm = (order ?? "desc").ToLowerInvariant();

            // Cache key inclui os filtros para evitar hit incorreto entre combinações distintas.
            var versao   = cache is not null ? await cache.GetStringAsync(VersaoKey(empresaId)) ?? "0" : "0";
            var filterKey = $"s={status?.ToString() ?? "_"}_np={(semPreco ? "1" : "0")}_c={categoriaId?.ToString() ?? "_"}";
            var cacheKey = $"produtos_paginados_{empresaId}_v{versao}_{page}_{pageSize}_{sortNorm}_{orderNorm}_{filterKey}";

            if (cache is not null)
            {
                try
                {
                    var cachedData = await cache.GetStringAsync(cacheKey);
                    if (!string.IsNullOrEmpty(cachedData))
                    {
                        var entry = JsonSerializer.Deserialize<PaginacaoCacheEntry>(cachedData);
                        if (entry is not null)
                            return (entry.Produtos, entry.TotalCount);
                    }
                }
                catch { await cache.RemoveAsync(cacheKey); }
            }

            var query = dbContext.Produtos
                .AsNoTracking()
                .Where(p => p.EmpresaId == empresaId);

            // Filtros server-side — aplicados ANTES do Count para total consistente
            // com a paginação entregue ao cliente.
            if (status.HasValue)
                query = query.Where(p => p.Status == status.Value);

            if (categoriaId.HasValue)
                query = query.Where(p => p.CategoriaId == categoriaId.Value);

            if (semPreco)
                query = query.Where(p => p.PrecoReferencia == null || p.PrecoReferencia.Valor == 0m);

            var totalCount = await query.CountAsync();

            query = (sortNorm, orderNorm) switch
            {
                ("nome",       "asc")  => query.OrderBy(p => p.Nome).ThenByDescending(p => p.Id),
                ("nome",       _)      => query.OrderByDescending(p => p.Nome).ThenByDescending(p => p.Id),
                ("criadoem",   "asc")  => query.OrderBy(p => p.CriadoEm).ThenByDescending(p => p.Id),
                ("criadoem",   _)      => query.OrderByDescending(p => p.CriadoEm).ThenByDescending(p => p.Id),
                ("alteradoem", "asc")  => query.OrderBy(p => p.AlteradoEm).ThenByDescending(p => p.Id),
                _                      => query.OrderByDescending(p => p.AlteradoEm)
                                                .ThenByDescending(p => p.CriadoEm)
                                                .ThenByDescending(p => p.Id)
            };

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

        public async Task<IReadOnlyList<string>> GetMarcasAsync(Guid empresaId, string? filtro = null, int max = 20)
        {
            var query = dbContext.Produtos
                .AsNoTracking()
                .Where(p => p.EmpresaId == empresaId && p.Marca != null && p.Marca.Trim() != "");

            if (!string.IsNullOrWhiteSpace(filtro))
                query = query.Where(p => EF.Functions.ILike(p.Marca!, $"%{filtro}%"));

            return await query
                .Select(p => p.Marca!)
                .Distinct()
                .OrderBy(m => m)
                .Take(max)
                .ToListAsync();
        }

        public async Task InsertAsync(Produto produto)
        {
            await dbContext.Produtos.AddAsync(produto);
            await InvalidarCacheAsync(produto.EmpresaId);
        }

        public Task<int> CountByEmpresaAsync(Guid empresaId) =>
            dbContext.Produtos.AsNoTracking()
                .CountAsync(p => p.EmpresaId == empresaId);

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
