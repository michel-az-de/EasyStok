using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Entities;
using EasyStock.Domain.ValueObjects;
using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Infra.Postgre.Repositories
{
    public sealed class ProdutoVariacaoRepository(EasyStockDbContext dbContext)
        : IProdutoVariacaoRepository
    {
        public Task<ProdutoVariacao?> GetByIdAsync(Guid id) =>
            dbContext.ProdutosVariacao.FirstOrDefaultAsync(v => v.Id == id);

        public Task<ProdutoVariacao?> GetByIdAsync(Guid empresaId, Guid produtoId, Guid id) =>
            dbContext.ProdutosVariacao
                .AsNoTracking()
                .FirstOrDefaultAsync(v => v.EmpresaId == empresaId && v.ProdutoId == produtoId && v.Id == id);

        public async Task<IEnumerable<ProdutoVariacao>> GetByProdutoAsync(Guid empresaId, Guid produtoId) =>
            await dbContext.ProdutosVariacao
                .AsNoTracking()
                .Where(v => v.EmpresaId == empresaId && v.ProdutoId == produtoId)
                .OrderBy(v => v.Nome)
                .ToListAsync();

        public Task<bool> ExistsSkuAsync(Guid empresaId, string sku, Guid? ignoreVariacaoId = null)
        {
            var skuObj = CodigoSku.From(sku.Trim());

            return dbContext.ProdutosVariacao
                .AsNoTracking()
                .Where(v => v.EmpresaId == empresaId && v.Sku == skuObj)
                .Where(v => !ignoreVariacaoId.HasValue || v.Id != ignoreVariacaoId.Value)
                .AnyAsync();
        }

        public async Task<IEnumerable<ProdutoVariacao>> SearchAsync(Guid empresaId, string termo, int maxResults = 20)
        {
            termo = termo.Trim();
            if (string.IsNullOrWhiteSpace(termo)) return [];

            var pattern = $"%{termo}%";

            CodigoSku? skuExato = null;
            try { skuExato = CodigoSku.From(termo); } catch (ArgumentException) { /* termo invalido para SKU */ }

            return await dbContext.ProdutosVariacao
                .AsNoTracking()
                .Where(v => v.EmpresaId == empresaId &&
                    (EF.Functions.ILike(v.Nome, pattern) ||
                     (v.Cor != null && EF.Functions.ILike(v.Cor, pattern)) ||
                     (v.Tamanho != null && EF.Functions.ILike(v.Tamanho, pattern)) ||
                     (v.DescricaoComercial != null && EF.Functions.ILike(v.DescricaoComercial, pattern)) ||
                     (skuExato != null && v.Sku == skuExato) ||
                     (v.CodigoBarras != null && EF.Functions.ILike(v.CodigoBarras, pattern))))
                .Take(maxResults)
                .ToListAsync();
        }

        public Task InsertAsync(ProdutoVariacao variacao) =>
            dbContext.ProdutosVariacao.AddAsync(variacao).AsTask();

        public Task UpdateAsync(ProdutoVariacao variacao)
        {
            dbContext.ProdutosVariacao.Update(variacao);
            return Task.CompletedTask;
        }

        public async Task DeleteAsync(Guid id)
        {
            var entity = await dbContext.ProdutosVariacao.FindAsync(id);
            if (entity is not null)
                dbContext.ProdutosVariacao.Remove(entity);
        }
    }
}
