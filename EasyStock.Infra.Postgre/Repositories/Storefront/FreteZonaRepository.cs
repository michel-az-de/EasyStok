using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Domain.Entities.Storefront;
using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Infra.Postgre.Repositories.Storefront;

public sealed class FreteZonaRepository(EasyStockDbContext db) : IFreteZonaRepository
{
    public Task<FreteZona?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        db.FreteZonas.FirstOrDefaultAsync(z => z.Id == id, ct);

    public async Task<IReadOnlyList<FreteZona>> GetAtivasDoStorefrontOrdenadasAsync(Guid storefrontId, CancellationToken ct = default) =>
        await db.FreteZonas
            .AsNoTracking()
            .Where(z => z.StorefrontId == storefrontId && z.Ativa)
            .OrderBy(z => z.Ordem)
            .ThenBy(z => z.Id)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<FreteZona>> GetTodasDoStorefrontAsync(Guid storefrontId, CancellationToken ct = default) =>
        await db.FreteZonas
            .AsNoTracking()
            .Where(z => z.StorefrontId == storefrontId)
            .OrderBy(z => z.Ordem)
            .ThenBy(z => z.Id)
            .ToListAsync(ct);

    public Task AddAsync(FreteZona zona, CancellationToken ct = default)
    {
        db.FreteZonas.Add(zona);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(FreteZona zona, CancellationToken ct = default)
    {
        db.FreteZonas.Update(zona);
        return Task.CompletedTask;
    }
}
