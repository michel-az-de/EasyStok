using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Entities;
using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Infra.Postgre.Repositories
{
    public sealed class ItemEstoqueRepository(EasyStockDbContext dbContext)
        : BaseRepository<ItemEstoque>(dbContext), IItemEstoqueRepository
    {
        public async Task<IEnumerable<ItemEstoque>> SearchAsync(Guid empresaId, string termo)
        {
            termo = termo.Trim();
            if (string.IsNullOrWhiteSpace(termo)) return [];

            var pattern = $"%{termo}%";

            return await DbContext.ItensEstoque
                .AsNoTracking()
                .Where(i => i.EmpresaId == empresaId &&
                    ((i.CodigoInterno != null && EF.Functions.ILike(i.CodigoInterno, pattern)) ||
                     (i.CodigoMarketplace != null && EF.Functions.ILike(i.CodigoMarketplace, pattern)) ||
                     (i.ChavePesquisa != null && EF.Functions.ILike(i.ChavePesquisa, pattern)) ||
                     (i.VariacaoDescricao != null && EF.Functions.ILike(i.VariacaoDescricao, pattern)) ||
                     (i.Cor != null && EF.Functions.ILike(i.Cor, pattern)) ||
                     (i.Tamanho != null && EF.Functions.ILike(i.Tamanho, pattern)) ||
                     (i.DescricaoAnuncio != null && EF.Functions.ILike(i.DescricaoAnuncio, pattern))))
                .ToListAsync();
        }
    }
}
