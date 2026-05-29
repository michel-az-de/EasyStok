using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Infra.Postgre.Repositories;

public sealed class IdempotencyKeyRepository(EasyStockDbContext dbContext) : IIdempotencyKeyRepository
{
    public Task<IdempotencyKey?> GetActiveAsync(string key, Guid empresaId, string metodoRecurso, CancellationToken ct = default)
    {
        var agora = DateTime.UtcNow;
        return dbContext.IdempotencyKeys
            .AsNoTracking()
            .FirstOrDefaultAsync(x =>
                x.Key == key &&
                x.EmpresaId == empresaId &&
                x.MetodoRecurso == metodoRecurso &&
                x.ExpiraEm > agora,
                ct);
    }

    public async Task<IdempotencyKey> SaveAsync(IdempotencyKey entry, CancellationToken ct = default)
    {
        // UPSERT atomico via constraint unica (Key, EmpresaId, MetodoRecurso).
        // Em caso de DbUpdateException (duplicate key, concurrent insert), busca
        // a entrada existente e devolve — o cliente vencedor "ganha".
        try
        {
            await dbContext.IdempotencyKeys.AddAsync(entry, ct);
            await dbContext.SaveChangesAsync(ct);
            return entry;
        }
        catch (DbUpdateException)
        {
            dbContext.Entry(entry).State = EntityState.Detached;
            var existing = await GetActiveAsync(entry.Key, entry.EmpresaId, entry.MetodoRecurso, ct);
            return existing ?? entry;
        }
    }

    public Task<int> CleanupExpiredAsync(DateTime referencia, CancellationToken ct = default) =>
        dbContext.IdempotencyKeys
            .Where(x => x.ExpiraEm <= referencia)
            .ExecuteDeleteAsync(ct);
}
