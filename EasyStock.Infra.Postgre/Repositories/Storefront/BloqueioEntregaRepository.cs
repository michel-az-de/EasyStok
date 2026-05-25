using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Domain.Entities.Storefront;
using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Infra.Postgre.Repositories.Storefront;

public sealed class BloqueioEntregaRepository(EasyStockDbContext db) : IBloqueioEntregaRepository
{
    public Task<BloqueioEntrega?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        db.BloqueiosEntrega.FirstOrDefaultAsync(b => b.Id == id, ct);

    public async Task<IReadOnlyList<BloqueioEntrega>> GetByStorefrontPeriodoAsync(
        Guid storefrontId,
        DateOnly inicio,
        DateOnly fim,
        CancellationToken ct = default) =>
        await db.BloqueiosEntrega
            .AsNoTracking()
            .Where(b => b.StorefrontId == storefrontId && b.Data >= inicio && b.Data <= fim)
            .OrderBy(b => b.Data)
            .ToListAsync(ct);

    public Task AddAsync(BloqueioEntrega bloqueio, CancellationToken ct = default)
    {
        db.BloqueiosEntrega.Add(bloqueio);
        return Task.CompletedTask;
    }
}
