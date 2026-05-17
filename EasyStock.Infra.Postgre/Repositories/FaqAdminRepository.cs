using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Infra.Postgre.Repositories
{
    public sealed class FaqAdminRepository(EasyStockDbContext db) : IFaqAdminRepository
    {
        public async Task<IReadOnlyList<FaqCategoria>> ListarCategoriasAsync(CancellationToken ct = default)
        {
            return await db.FaqCategorias
                .AsNoTracking()
                .OrderBy(c => c.Ordem)
                .ThenBy(c => c.Nome)
                .ToListAsync(ct);
        }

        public Task<FaqCategoria?> ObterCategoriaAsync(Guid id, CancellationToken ct = default)
        {
            return db.FaqCategorias.FirstOrDefaultAsync(c => c.Id == id, ct);
        }

        public Task<bool> CategoriaSlugExisteAsync(string slug, Guid? excetoId, CancellationToken ct = default)
        {
            var s = (slug ?? string.Empty).ToLowerInvariant();
            return db.FaqCategorias
                .AsNoTracking()
                .AnyAsync(c => c.Slug == s && (excetoId == null || c.Id != excetoId), ct);
        }

        public async Task InserirCategoriaAsync(FaqCategoria categoria, CancellationToken ct = default)
        {
            await db.FaqCategorias.AddAsync(categoria, ct);
        }

        public Task AtualizarCategoriaAsync(FaqCategoria categoria, CancellationToken ct = default)
        {
            db.FaqCategorias.Update(categoria);
            return Task.CompletedTask;
        }

        public async Task<(IReadOnlyList<FaqItem> Itens, int Total)> ListarItensAsync(
            FaqStatus? status,
            Guid? categoriaId,
            string? busca,
            int page,
            int pageSize,
            CancellationToken ct = default)
        {
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 100);

            var query = db.FaqItens
                .AsNoTracking()
                .Include(i => i.Categoria)
                .AsQueryable();

            if (status.HasValue)
                query = query.Where(i => i.Status == status.Value);

            if (categoriaId.HasValue && categoriaId.Value != Guid.Empty)
                query = query.Where(i => i.CategoriaId == categoriaId.Value);

            if (!string.IsNullOrWhiteSpace(busca))
            {
                var t = busca.Trim().ToLowerInvariant();
                query = query.Where(i =>
                    EF.Functions.ILike(i.Titulo, $"%{t}%")
                    || EF.Functions.ILike(i.ConteudoBusca, $"%{t}%"));
            }

            var total = await query.CountAsync(ct);
            var itens = await query
                .OrderByDescending(i => i.AtualizadoEm)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync(ct);

            return (itens, total);
        }

        public Task<FaqItem?> ObterItemAsync(Guid id, CancellationToken ct = default)
        {
            return db.FaqItens.Include(i => i.Categoria).FirstOrDefaultAsync(i => i.Id == id, ct);
        }

        public Task<bool> ItemSlugExisteAsync(Guid categoriaId, string slug, Guid? excetoId, CancellationToken ct = default)
        {
            var s = (slug ?? string.Empty).ToLowerInvariant();
            return db.FaqItens
                .AsNoTracking()
                .AnyAsync(i => i.CategoriaId == categoriaId && i.Slug == s && (excetoId == null || i.Id != excetoId), ct);
        }

        public async Task InserirItemAsync(FaqItem item, CancellationToken ct = default)
        {
            await db.FaqItens.AddAsync(item, ct);
        }

        public Task AtualizarItemAsync(FaqItem item, CancellationToken ct = default)
        {
            db.FaqItens.Update(item);
            return Task.CompletedTask;
        }
    }
}
