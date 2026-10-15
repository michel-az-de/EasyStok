using EasyStock.Domain.Entities.Storefront;

namespace EasyStock.Application.Ports.Output.Persistence.Storefront;

/// <summary>
/// Seções do cardápio (ADR-0035, M1.3 #1483). O escopo de tenant é o storefront: o caller resolve o
/// storefront da empresa logada antes, e toda consulta filtra por ele.
/// </summary>
public interface ICardapioSecaoRepository
{
    /// <summary>Todas as seções do storefront, rastreadas (são poucas; renumerar grava várias).</summary>
    Task<IReadOnlyList<CardapioSecao>> GetDoStorefrontAsync(Guid storefrontId, CancellationToken ct = default);

    Task<CardapioSecao?> GetByIdAsync(Guid storefrontId, Guid secaoId, CancellationToken ct = default);

    /// <summary>Itens (arquivados inclusive) por seção do storefront. Seção sem item não aparece.</summary>
    Task<IReadOnlyDictionary<Guid, int>> ContarItensPorSecaoAsync(Guid storefrontId, CancellationToken ct = default);

    Task AddAsync(CardapioSecao secao, CancellationToken ct = default);
    Task RemoveAsync(CardapioSecao secao, CancellationToken ct = default);
}
