using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Domain.Entities.Storefront;
using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Infra.Postgre.Repositories.Storefront;

public sealed class StorefrontFaleConoscoRepository(EasyStockDbContext db) : IStorefrontFaleConoscoRepository
{
    public Task<StorefrontFaleConosco?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        db.StorefrontFaleConoscos.FirstOrDefaultAsync(f => f.Id == id, ct);

    public async Task<IReadOnlyList<StorefrontFaleConosco>> GetByStorefrontAsync(
        Guid storefrontId,
        int max = 100,
        CancellationToken ct = default) =>
        await db.StorefrontFaleConoscos
            .AsNoTracking()
            .Where(f => f.StorefrontId == storefrontId)
            .OrderByDescending(f => f.CriadoEm)
            .Take(max)
            .ToListAsync(ct);

    public Task AddAsync(StorefrontFaleConosco mensagem, CancellationToken ct = default)
    {
        db.StorefrontFaleConoscos.Add(mensagem);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(StorefrontFaleConosco mensagem, CancellationToken ct = default)
    {
        db.StorefrontFaleConoscos.Update(mensagem);
        return Task.CompletedTask;
    }
}
