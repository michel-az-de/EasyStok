using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Entities;
using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Infra.Postgre.Repositories
{
    public sealed class ProdutoVariacaoRepository(EasyStockDbContext dbContext)
        : IProdutoVariacaoRepository
    {
        public Task<ProdutoVariacao?> GetByIdAsync(Guid id) =>
            dbContext.ProdutosVariacao.FirstOrDefaultAsync(v => v.Id == id);

        public async Task<IEnumerable<ProdutoVariacao>> SearchAsync(Guid empresaId, string termo)
        {
            termo = termo.Trim();
            if (string.IsNullOrWhiteSpace(termo)) return [];

            var pattern = $"%{termo}%";

            return await dbContext.ProdutosVariacao
                .AsNoTracking()
                .Where(v => v.EmpresaId == empresaId &&
                    (EF.Functions.ILike(v.Nome, pattern) ||
                     (v.Cor != null && EF.Functions.ILike(v.Cor, pattern)) ||
                     (v.Tamanho != null && EF.Functions.ILike(v.Tamanho, pattern)) ||
                     (v.DescricaoComercial != null && EF.Functions.ILike(v.DescricaoComercial, pattern)) ||
                     EF.Functions.ILike(EF.Property<string?>(v, nameof(ProdutoVariacao.Sku))!, pattern) ||
                    (v.CodigoBarras != null && EF.Functions.ILike(v.CodigoBarras, pattern))))
                .ToListAsync();
        }

        public Task InsertAsync(ProdutoVariacao variacao) =>
            dbContext.ProdutosVariacao.AddAsync(variacao).AsTask();
    }
}
