using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Entities;
using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Infra.Postgre.Repositories
{
    public sealed class LoteRepository(EasyStockDbContext db) : ILoteRepository
    {
        public Task<Lote?> GetByIdAsync(Guid empresaId, Guid id) =>
            db.Lotes.FirstOrDefaultAsync(l => l.EmpresaId == empresaId && l.Id == id);

        public Task<Lote?> GetByIdWithDetailsAsync(Guid empresaId, Guid id) =>
            db.Lotes
                .Include(l => l.Itens)
                .Include(l => l.Etiquetas)
                .FirstOrDefaultAsync(l => l.EmpresaId == empresaId && l.Id == id);

        public Task<Lote?> FindByCodigoAsync(Guid empresaId, string codigo) =>
            db.Lotes.FirstOrDefaultAsync(l => l.EmpresaId == empresaId && l.Codigo == codigo);

        public Task<Lote?> FindByMobileBatchIdAsync(Guid empresaId, string mobileBatchId) =>
            db.Lotes.FirstOrDefaultAsync(l =>
                l.EmpresaId == empresaId && l.MobileBatchId == mobileBatchId);

        public async Task<(IEnumerable<Lote> items, int total)> ListAsync(
            Guid empresaId, int page, int pageSize,
            string? status = null, DateTime? desde = null, DateTime? ate = null,
            string? search = null, string? sort = "dataproducao", string? order = "desc")
        {
            var q = db.Lotes.AsNoTracking().Where(l => l.EmpresaId == empresaId);
            if (!string.IsNullOrWhiteSpace(status)) q = q.Where(l => l.Status == status);
            if (desde.HasValue) q = q.Where(l => l.DataProducao >= desde.Value);
            if (ate.HasValue)   q = q.Where(l => l.DataProducao <= ate.Value);
            if (!string.IsNullOrWhiteSpace(search))
            {
                var termo = search.Trim();
                q = q.Where(l =>
                    EF.Functions.ILike(l.Codigo, $"%{termo}%") ||
                    (l.OperadorNome != null && EF.Functions.ILike(l.OperadorNome, $"%{termo}%")) ||
                    (l.Observacoes != null && EF.Functions.ILike(l.Observacoes, $"%{termo}%")));
            }

            var total = await q.CountAsync();
            var desc = string.Equals(order, "desc", StringComparison.OrdinalIgnoreCase);
            q = sort?.ToLowerInvariant() switch
            {
                "codigo" => desc ? q.OrderByDescending(l => l.Codigo) : q.OrderBy(l => l.Codigo),
                "status" => desc ? q.OrderByDescending(l => l.Status) : q.OrderBy(l => l.Status),
                _        => desc ? q.OrderByDescending(l => l.DataProducao) : q.OrderBy(l => l.DataProducao),
            };
            var items = await q.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
            return (items, total);
        }

        public async Task<int> GetNextSequencialDoDiaAsync(Guid empresaId, DateOnly data)
        {
            var inicio = data.ToDateTime(TimeOnly.MinValue);
            var fim    = data.AddDays(1).ToDateTime(TimeOnly.MinValue);
            return await db.Lotes.AsNoTracking()
                .Where(l => l.EmpresaId == empresaId
                         && l.DataProducao >= inicio && l.DataProducao < fim)
                .CountAsync() + 1;
        }

        public Task AddAsync(Lote lote) { db.Lotes.Add(lote); return Task.CompletedTask; }
        public Task UpdateAsync(Lote lote) { db.Lotes.Update(lote); return Task.CompletedTask; }

        public Task AddItemAsync(LoteItem item) { db.Set<LoteItem>().Add(item); return Task.CompletedTask; }
        public Task RemoveItemAsync(Guid itemId) =>
            db.Set<LoteItem>().Where(i => i.Id == itemId).ExecuteDeleteAsync();

        public Task AddEtiquetaAsync(LoteEtiqueta e) { db.Set<LoteEtiqueta>().Add(e); return Task.CompletedTask; }

        public Task<LoteEtiqueta?> FindEtiquetaPorCodigoAsync(Guid empresaId, string codigo) =>
            db.Set<LoteEtiqueta>()
                .Include(e => e.Lote)
                .FirstOrDefaultAsync(e => e.Codigo == codigo && e.Lote!.EmpresaId == empresaId);

        public Task UpdateEtiquetaAsync(LoteEtiqueta e) { db.Set<LoteEtiqueta>().Update(e); return Task.CompletedTask; }
    }
}
