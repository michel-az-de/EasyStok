using EasyStock.Domain.Entities.Storefront;

namespace EasyStock.Application.Ports.Output.Persistence.Storefront;

/// <summary>
/// Repo de <see cref="CheckoutIdempotency"/> — previne duplo-clique em "Pagar"
/// gerar Faturas + InitPoints duplicados (R5).
/// </summary>
public interface ICheckoutIdempotencyRepository
{
    Task<CheckoutIdempotency?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>Lookup por (Key, ContentHash) — chave composta exata.</summary>
    Task<CheckoutIdempotency?> GetByKeyHashAsync(Guid key, string contentHash, CancellationToken ct = default);

    /// <summary>Lookup por Key apenas — para detectar conteúdo alterado pelo client.</summary>
    Task<IReadOnlyList<CheckoutIdempotency>> GetByKeyAsync(Guid key, CancellationToken ct = default);

    Task AddAsync(CheckoutIdempotency registro, CancellationToken ct = default);
    Task UpdateAsync(CheckoutIdempotency registro, CancellationToken ct = default);
}
