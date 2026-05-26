using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Domain.Entities.Storefront;
using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace EasyStock.Infra.Postgre.Repositories.Storefront;

/// <summary>
/// EF Repository de <see cref="CheckoutIdempotency"/>. Entity não tem EmpresaId,
/// então é cross-tenant by-design (idempotency key é UUID — colisão ~impossível).
///
/// <para>
/// <see cref="TentarReservarAsync"/> usa dedup atômico via unique constraint
/// <c>uq_checkout_idempotency_key_hash</c> + catch de <see cref="DbUpdateException"/>
/// SQLSTATE 23505. Substitui o padrão SELECT-then-INSERT que abre TOCTOU
/// em duplo-clique de "Pagar" concorrente.
/// </para>
/// </summary>
public sealed class CheckoutIdempotencyRepository(EasyStockDbContext db) : ICheckoutIdempotencyRepository
{
    public Task<CheckoutIdempotency?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        db.CheckoutsIdempotency.FirstOrDefaultAsync(c => c.Id == id, ct);

    public Task<CheckoutIdempotency?> GetByKeyHashAsync(
        Guid key,
        string contentHash,
        CancellationToken ct = default)
    {
        // Normaliza igual ao CheckoutIdempotency.Criar — caller pode passar hash
        // bruto do payload (vindo do request) sem precisar conhecer normalização.
        var hashNorm = (contentHash ?? string.Empty).Trim().ToLowerInvariant();
        return db.CheckoutsIdempotency
            .FirstOrDefaultAsync(c => c.Key == key && c.ContentHash == hashNorm, ct);
    }

    public async Task<IReadOnlyList<CheckoutIdempotency>> GetByKeyAsync(Guid key, CancellationToken ct = default) =>
        await db.CheckoutsIdempotency
            .Where(c => c.Key == key)
            .OrderByDescending(c => c.CriadoEm)
            .ToListAsync(ct);

    public Task AddAsync(CheckoutIdempotency registro, CancellationToken ct = default)
    {
        db.CheckoutsIdempotency.Add(registro);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(CheckoutIdempotency registro, CancellationToken ct = default)
    {
        db.CheckoutsIdempotency.Update(registro);
        return Task.CompletedTask;
    }

    public async Task<(bool reservado, CheckoutIdempotency registro)> TentarReservarAsync(
        CheckoutIdempotency proposta,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(proposta);

        try
        {
            await db.CheckoutsIdempotency.AddAsync(proposta, ct);
            await db.SaveChangesAsync(ct);
            return (true, proposta);
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            // Duplo-clique: outra request com (Key, ContentHash) idênticos chegou primeiro.
            // Detacha pra não tentar novamente, busca o vencedor.
            db.Entry(proposta).State = EntityState.Detached;
            var existente = await GetByKeyHashAsync(proposta.Key, proposta.ContentHash, ct);
            return (false, existente ?? throw new InvalidOperationException(
                $"Unique violation em (key={proposta.Key}, hash={proposta.ContentHash}) mas registro existente não encontrado."));
        }
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException pg && pg.SqlState == "23505";
}
