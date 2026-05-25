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

    /// <summary>
    /// Tenta reservar a chave de idempotência: INSERT atômico cobrindo race entre
    /// duplo-clique. Se já existe registro com mesma (Key, ContentHash), retorna
    /// <c>(false, existente)</c> sem lançar. Senão retorna <c>(true, proposta)</c>.
    ///
    /// <para>
    /// <strong>Chama SaveChanges internamente</strong> — atomicidade depende da
    /// unique constraint <c>uq_checkout_idempotency_key_hash</c> disparar
    /// <see cref="Microsoft.EntityFrameworkCore.DbUpdateException"/>, o que só
    /// acontece no flush. Caller deve assumir que o registro está persistido.
    /// </para>
    /// </summary>
    Task<(bool reservado, CheckoutIdempotency registro)> TentarReservarAsync(
        CheckoutIdempotency proposta,
        CancellationToken ct = default);
}
