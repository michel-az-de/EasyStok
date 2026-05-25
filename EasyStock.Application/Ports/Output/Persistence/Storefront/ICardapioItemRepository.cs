using EasyStock.Domain.Entities.Storefront;

namespace EasyStock.Application.Ports.Output.Persistence.Storefront;

public interface ICardapioItemRepository
{
    Task<CardapioItem?> GetByIdAsync(Guid storefrontId, Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<CardapioItem>> GetVisiveisDoStorefrontAsync(Guid storefrontId, CancellationToken ct = default);
    Task<IReadOnlyList<CardapioItem>> GetTodosDoStorefrontAsync(Guid storefrontId, CancellationToken ct = default);
    Task AddAsync(CardapioItem item, CancellationToken ct = default);
    Task UpdateAsync(CardapioItem item, CancellationToken ct = default);
}
