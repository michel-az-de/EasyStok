using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Infra.Postgre.Repositories
{
    public sealed class ContagemRepository(EasyStockDbContext dbContext) : IContagemRepository
    {
        public Task<Contagem?> GetByIdAsync(Guid empresaId, Guid id) =>
            dbContext.Contagens
                .FirstOrDefaultAsync(c => c.EmpresaId == empresaId && c.Id == id);

        public Task<Contagem?> GetByIdComItensAsync(Guid empresaId, Guid id) =>
            dbContext.Contagens
                .Include(c => c.Itens)
                .FirstOrDefaultAsync(c => c.EmpresaId == empresaId && c.Id == id);

        public Task<ItemContagem?> GetItemAsync(Guid empresaId, Guid itemContagemId) =>
            dbContext.ItensContagem
                .FirstOrDefaultAsync(i => i.EmpresaId == empresaId && i.Id == itemContagemId);

        public Task AddAsync(Contagem contagem) =>
            dbContext.Contagens.AddAsync(contagem).AsTask();

        public Task UpdateAsync(Contagem contagem)
        {
            dbContext.Contagens.Update(contagem);
            return Task.CompletedTask;
        }

        public async Task<IReadOnlyList<ItemEstoque>> GetLotesDoEscopoAsync(
            Guid empresaId, EscopoContagem escopo, Guid? escopoRefId)
        {
            var query = dbContext.ItensEstoque
                .AsNoTracking()
                .Where(i => i.EmpresaId == empresaId && i.Status != StatusItemEstoque.Descartado);

            query = escopo switch
            {
                EscopoContagem.Categoria =>
                    query.Where(i => i.Produto != null && i.Produto.CategoriaId == escopoRefId),
                EscopoContagem.Loja =>
                    query.Where(i => i.LojaId == escopoRefId),
                _ => query, // Todos
            };

            return await query.ToListAsync();
        }
    }
}
