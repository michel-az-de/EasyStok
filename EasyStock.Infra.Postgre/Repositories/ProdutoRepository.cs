using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Entities;
using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Infra.Postgre.Repositories
{
    // Proposed PostgreSQL index for performance:
    // CREATE INDEX idx_produtos_empresa_nome ON produtos (empresaid, nome);

    public sealed class ProdutoRepository(EasyStockDbContext dbContext)
        : BaseRepository<Produto>(dbContext), IProdutoRepository
    {
        public async Task<IEnumerable<Produto>> SearchAsync(Guid empresaId, string termo)
        {
            termo = termo.Trim();
            if (string.IsNullOrWhiteSpace(termo)) return [];

            var pattern = $"%{termo}%";

            return await DbContext.Produtos
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
            var query = DbContext.Produtos
                .AsNoTracking()
                .Where(p => p.EmpresaId == empresaId);

            var totalCount = await query.CountAsync();
            var produtos = await query
                .OrderBy(p => p.Nome)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return (produtos, totalCount);
        }
    }
}
