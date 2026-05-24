using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Domain.Entities.Storefront;
using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Infra.Postgre.Repositories.Storefront;

/// <summary>
/// EF Repository de <see cref="CheckoutIdempotency"/>. Entity não tem EmpresaId,
/// então é cross-tenant by-design (idempotency key é UUID — colisão ~impossível).
/// </summary>
public sealed class CheckoutIdempotencyRepository(EasyStockDbContext db) : ICheckoutIdempotencyRepository
{
    public Task<CheckoutIdempotency?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        db.CheckoutsIdempotency.FirstOrDefaultAsync(c => c.Id == id, ct);

    public Task<CheckoutIdempotency?> GetByKeyHashAsync(
        Guid key,
        string contentHash,
        CancellationToken ct = default)
    {
        var hashNorm = (contentHash ?? string.Empty).Trim().ToLowerInvariant();
        return db.CheckoutsIdempotency
            .FirstOrDefaultAsync(c => c.Key == key && c.ContentHash == hashNorm, ct);
    }

    public async Task<IReadOnlyList<CheckoutIdempotency>> GetByKeyAsync(Guid key, CancellationToken ct = default) =>
        await db.CheckoutsIdempotency
            .Where(c => c.Key == key)
            .OrderByDescending(c => c.CriadoEm)
            .ToListAsync(ct);

    public Task AddAsync(CheckoutIdempotency registro, CancellationToken ct = default)
    {
        db.CheckoutsIdempotency.Add(registro);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(CheckoutIdempotency registro, CancellationToken ct = default)
    {
        db.CheckoutsIdempotency.Update(registro);
        return Task.CompletedTask;
    }
}
