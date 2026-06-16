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
        // CardapioItem não tem EmpresaId (logo não recebe o global query filter de tenant) nem
        // navegação para Storefront — por isso o EXISTS em db.Storefronts. O `s.EmpresaId == emp`
        // é REDUNDANTE com o global query filter que já incide sobre db.Storefronts (s.EmpresaId ==
        // CurrentTenantId), mas é mantido de propósito como defense-in-depth: o escopo do item passa
        // a ser garantido aqui de forma explícita, independente do contexto de tenant do request
        // (ADR-0031 §3; auditoria 2026-06-11, finding p3 — manter ambos é intencional).
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

    public Task RemoveAsync(CardapioItem item, CancellationToken ct = default)
    {
        db.CardapioItens.Remove(item);
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
