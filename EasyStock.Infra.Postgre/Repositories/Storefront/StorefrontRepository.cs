using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore;
using StorefrontEntity = EasyStock.Domain.Entities.Storefront.Storefront;

namespace EasyStock.Infra.Postgre.Repositories.Storefront;

public sealed class StorefrontRepository(EasyStockDbContext db) : IStorefrontRepository
{
    public Task<StorefrontEntity?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        db.Storefronts.FirstOrDefaultAsync(s => s.Id == id, ct);

    public Task<StorefrontEntity?> GetBySlugAsync(string slug, CancellationToken ct = default) =>
        db.Storefronts.AsNoTracking().FirstOrDefaultAsync(s => s.Slug == slug, ct);

    public Task<StorefrontEntity?> GetByDominioCustomAsync(string dominioCustom, CancellationToken ct = default) =>
        db.Storefronts.AsNoTracking().FirstOrDefaultAsync(s => s.DominioCustom == dominioCustom, ct);

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
}
