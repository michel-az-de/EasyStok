using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Domain.Entities.Storefront;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Infra.Postgre.Repositories.Storefront;

public sealed class CardapioSecaoRepository(EasyStockDbContext db) : ICardapioSecaoRepository
{
    public async Task<IReadOnlyList<CardapioSecao>> GetDoStorefrontAsync(Guid storefrontId, CancellationToken ct = default) =>
        await db.CardapioSecoes
            .Where(s => s.StorefrontId == storefrontId)
            .OrderBy(s => s.OrdemExibicao).ThenBy(s => s.CriadoEm).ThenBy(s => s.Id)
            .ToListAsync(ct);

    public Task<CardapioSecao?> GetByIdAsync(Guid storefrontId, Guid secaoId, CancellationToken ct = default) =>
        db.CardapioSecoes.FirstOrDefaultAsync(s => s.StorefrontId == storefrontId && s.Id == secaoId, ct);

    public async Task<IReadOnlyDictionary<Guid, int>> ContarItensPorSecaoAsync(Guid storefrontId, CancellationToken ct = default) =>
        await db.CardapioItens
            .Where(i => i.StorefrontId == storefrontId && i.SecaoId != null)
            .GroupBy(i => i.SecaoId!.Value)
            .Select(g => new { g.Key, Total = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Total, ct);

    public async Task AddAsync(CardapioSecao secao, CancellationToken ct = default) =>
        await db.CardapioSecoes.AddAsync(secao, ct);

    public Task RemoveAsync(CardapioSecao secao, CancellationToken ct = default)
    {
        db.CardapioSecoes.Remove(secao);
        return Task.CompletedTask;
    }
}
