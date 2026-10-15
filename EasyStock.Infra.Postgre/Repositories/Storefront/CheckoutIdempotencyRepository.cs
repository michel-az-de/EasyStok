using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Domain.Entities.Storefront;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Infra.Postgre.Repositories.Storefront;

/// <summary>
/// Reserva serializada por chave, inclusive quando o hash diverge. O coordenador calcula a chave
/// com loja e identidade verificada ou chave anônima; nunca reutiliza diretamente a chave pública em outro tenant.
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

    public Task RemoverSemRespostaAsync(Guid key, string hash, CancellationToken ct = default) =>
        db.CheckoutsIdempotency.Where(c => c.Key == key && c.ContentHash == hash && c.InitPoint == null).ExecuteDeleteAsync(ct);

    public async Task<(bool reservado, CheckoutIdempotency registro)> TentarReservarAsync(
        CheckoutIdempotency proposta, CancellationToken ct = default)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var chave = $"checkout-idempotency:{proposta.Key:N}";
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtext({chave}))", ct);
            var existente = await db.CheckoutsIdempotency.FirstOrDefaultAsync(c => c.Key == proposta.Key, ct);
            if (existente is not null)
            {
                await tx.CommitAsync(ct);
                return (false, existente);
            }
            await db.CheckoutsIdempotency.AddAsync(proposta, ct);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return (true, proposta);
        });
    }
}
