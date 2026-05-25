using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Domain.Entities.Storefront;
using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Infra.Postgre.Repositories.Storefront;

public sealed class CardapioItemRepository(EasyStockDbContext db) : ICardapioItemRepository
{
    public Task<CardapioItem?> GetByIdAsync(Guid storefrontId, Guid id, CancellationToken ct = default) =>
        db.CardapioItens
            .Include(c => c.Produto)
            .FirstOrDefaultAsync(c => c.StorefrontId == storefrontId && c.Id == id, ct);

    public async Task<IReadOnlyList<CardapioItem>> GetVisiveisDoStorefrontAsync(Guid storefrontId, CancellationToken ct = default) =>
        await db.CardapioItens
            .AsNoTracking()
            .Include(c => c.Produto)
            .Where(c => c.StorefrontId == storefrontId && c.Visivel)
            .OrderBy(c => c.OrdemExibicao)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<CardapioItem>> GetTodosDoStorefrontAsync(Guid storefrontId, CancellationToken ct = default) =>
        await db.CardapioItens
            .AsNoTracking()
            .Include(c => c.Produto)
            .Where(c => c.StorefrontId == storefrontId)
            .OrderBy(c => c.OrdemExibicao)
            .ToListAsync(ct);

    public Task AddAsync(CardapioItem item, CancellationToken ct = default)
    {
        db.CardapioItens.Add(item);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(CardapioItem item, CancellationToken ct = default)
    {
        db.CardapioItens.Update(item);
        return Task.CompletedTask;
    }
}
