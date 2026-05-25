using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Entities;
using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Infra.Postgre.Repositories
{
    public sealed class ListaComprasRepository(EasyStockDbContext db) : IListaComprasRepository
    {
        public Task<ListaCompras?> GetByIdAsync(Guid empresaId, Guid id) =>
            db.ListasCompras.FirstOrDefaultAsync(l => l.EmpresaId == empresaId && l.Id == id);

        public Task<ListaCompras?> GetByIdWithItemsAsync(Guid empresaId, Guid id) =>
            db.ListasCompras
                .Include(l => l.Itens)
                .FirstOrDefaultAsync(l => l.EmpresaId == empresaId && l.Id == id);

        public async Task<(IEnumerable<ListaCompras> items, int total)> ListAsync(
            Guid empresaId, int page, int pageSize,
            string? status = null, string? search = null)
        {
            var q = db.ListasCompras.AsNoTracking()
                .Include(l => l.Itens)
                .Where(l => l.EmpresaId == empresaId);
            if (!string.IsNullOrWhiteSpace(status)) q = q.Where(l => l.Status == status);
            if (!string.IsNullOrWhiteSpace(search))
            {
                var termo = search.Trim();
                q = q.Where(l =>
                    EF.Functions.ILike(l.Nome, $"%{termo}%") ||
                    (l.Observacoes != null && EF.Functions.ILike(l.Observacoes, $"%{termo}%")));
            }

            var total = await q.CountAsync();
            var items = await q.OrderByDescending(l => l.CriadoEm)
                .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
            return (items, total);
        }

        public Task<ListaCompras?> GetListaAbertaAsync(Guid empresaId, Guid? lojaId = null) =>
            db.ListasCompras
                .Include(l => l.Itens)
                .Where(l => l.EmpresaId == empresaId && l.Status == "aberta"
                         && (lojaId == null || l.LojaId == lojaId))
                .OrderByDescending(l => l.CriadoEm)
                .FirstOrDefaultAsync();

        public Task AddAsync(ListaCompras lista) { db.ListasCompras.Add(lista); return Task.CompletedTask; }
        public Task UpdateAsync(ListaCompras lista) { db.ListasCompras.Update(lista); return Task.CompletedTask; }

        public Task<ItemListaCompras?> GetItemAsync(Guid id) =>
            db.Set<ItemListaCompras>().FirstOrDefaultAsync(i => i.Id == id);

        public Task AddItemAsync(ItemListaCompras item) { db.Set<ItemListaCompras>().Add(item); return Task.CompletedTask; }
        public Task UpdateItemAsync(ItemListaCompras item) { db.Set<ItemListaCompras>().Update(item); return Task.CompletedTask; }
        public Task RemoveItemAsync(Guid itemId) =>
            db.Set<ItemListaCompras>().Where(i => i.Id == itemId).ExecuteDeleteAsync();
    }
}
