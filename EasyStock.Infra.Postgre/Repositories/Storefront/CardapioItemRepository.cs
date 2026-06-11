using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Domain.Entities.Storefront;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Infra.Postgre.Repositories.Storefront;

public sealed class CardapioItemRepository(EasyStockDbContext db) : ICardapioItemRepository
{
    public Task<CardapioItem?> GetByIdAsync(Guid storefrontId, Guid id, CancellationToken ct = default) =>
        db.CardapioItens
            .Include(c => c.Produto)
            .FirstOrDefaultAsync(c => c.StorefrontId == storefrontId && c.Id == id, ct);

    public Task<CardapioItem?> GetByIdAndScopeAsync(Guid storefrontId, Guid itemId, Guid? empresaId, CancellationToken ct = default)
    {
        var query = db.CardapioItens
            .Include(c => c.Produto)
            .Where(c => c.StorefrontId == storefrontId && c.Id == itemId);

        // Escopo de tenant: só itens cujo Storefront pertence à empresa. SuperAdmin (null) ignora.
        // CardapioItem não tem navegação para Storefront — filtro via EXISTS em db.Storefronts.
        if (empresaId.HasValue)
        {
            var emp = empresaId.Value;
            query = query.Where(c => db.Storefronts.Any(s => s.Id == c.StorefrontId && s.EmpresaId == emp));
        }

        return query.FirstOrDefaultAsync(ct);
    }

    public async Task<IReadOnlyList<CardapioItem>> GetVisiveisDoStorefrontAsync(Guid storefrontId, CancellationToken ct = default) =>
        await db.CardapioItens
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Include(c => c.Produto)
                .ThenInclude(p => p!.Categoria)
            .Where(c => c.StorefrontId == storefrontId && c.Visivel)
            .OrderBy(c => c.Produto!.Categoria!.Nome)
            .ThenBy(c => c.OrdemExibicao)
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

    public Task<int> ContarPorStorefrontAsync(Guid storefrontId, CancellationToken ct = default) =>
        db.CardapioItens.CountAsync(c => c.StorefrontId == storefrontId, ct);

    public async Task<IReadOnlyCollection<Guid>> GetProdutoIdsDoStorefrontAsync(Guid storefrontId, CancellationToken ct = default) =>
        await db.CardapioItens
            .AsNoTracking()
            .Where(c => c.StorefrontId == storefrontId && c.ProdutoId.HasValue)
            .Select(c => c.ProdutoId!.Value)   // Guid? → Guid: safe após filtro HasValue
            .ToListAsync(ct);
}
