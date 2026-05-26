using EasyStock.Domain.Entities.Storefront;

namespace EasyStock.Application.Ports.Output.Persistence.Storefront;

public interface ICardapioItemRepository
{
    Task<CardapioItem?> GetByIdAsync(Guid storefrontId, Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<CardapioItem>> GetVisiveisDoStorefrontAsync(Guid storefrontId, CancellationToken ct = default);
    Task<IReadOnlyList<CardapioItem>> GetTodosDoStorefrontAsync(Guid storefrontId, CancellationToken ct = default);
    Task AddAsync(CardapioItem item, CancellationToken ct = default);
    Task UpdateAsync(CardapioItem item, CancellationToken ct = default);

    /// <summary>Conta items (visíveis ou não) no storefront — admin listagem.</summary>
    Task<int> ContarPorStorefrontAsync(Guid storefrontId, CancellationToken ct = default);

    /// <summary>ProdutoIds já em uso pelo storefront — filtra dropdown ao adicionar.</summary>
    Task<IReadOnlyCollection<Guid>> GetProdutoIdsDoStorefrontAsync(Guid storefrontId, CancellationToken ct = default);
}
