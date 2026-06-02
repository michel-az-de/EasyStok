using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Infra.Postgre.Data;
using StorefrontEntity = EasyStock.Domain.Entities.Storefront.Storefront;

namespace EasyStock.Infra.Postgre.Repositories.Storefront;

public sealed class StorefrontRepository(EasyStockDbContext db) : IStorefrontRepository
{
    public Task<StorefrontEntity?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        db.Storefronts.FirstOrDefaultAsync(s => s.Id == id, ct);

    public Task<StorefrontEntity?> GetBySlugAsync(string slug, CancellationToken ct = default) =>
        db.Storefronts.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(s => s.Slug == slug, ct);

    public Task<StorefrontEntity?> GetByDominioCustomAsync(string dominioCustom, CancellationToken ct = default) =>
        db.Storefronts.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(s => s.DominioCustom == dominioCustom, ct);

    public Task<StorefrontEntity?> GetByEmpresaAsync(Guid empresaId, CancellationToken ct = default) =>
        db.Storefronts.FirstOrDefaultAsync(s => s.EmpresaId == empresaId, ct);

    public Task AddAsync(StorefrontEntity storefront, CancellationToken ct = default)
    {
        db.Storefronts.Add(storefront);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(StorefrontEntity storefront, CancellationToken ct = default)
    {
        db.Storefronts.Update(storefront);
        return Task.CompletedTask;
    }

    public async Task<(IReadOnlyList<StorefrontEntity> Itens, int Total)> ListarAdminAsync(
        int skip, int take, string? buscaSlug, bool? ativo, CancellationToken ct = default)
    {
        if (skip < 0) skip = 0;
        if (take <= 0) take = 20;
        if (take > 100) take = 100;

        var q = db.Storefronts.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(buscaSlug))
        {
            var termo = buscaSlug.Trim().ToLowerInvariant();
            q = q.Where(s => EF.Functions.ILike(s.Slug, $"%{termo}%")
                          || EF.Functions.ILike(s.TituloPublico, $"%{termo}%"));
        }

        if (ativo.HasValue)
        {
            q = q.Where(s => s.Ativo == ativo.Value);
        }

        var total = await q.CountAsync(ct);
        var itens = await q.OrderByDescending(s => s.CriadoEm).Skip(skip).Take(take).ToListAsync(ct);
        return (itens, total);
    }
}
