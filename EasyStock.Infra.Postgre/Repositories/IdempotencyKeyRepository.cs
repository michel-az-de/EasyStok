using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Infra.Postgre.Repositories;

public sealed class IdempotencyKeyRepository(EasyStockDbContext dbContext) : IIdempotencyKeyRepository
{
    public async Task<(IdempotencyKey Entry, bool Reservado)> TryBeginAsync(
        string key, Guid empresaId, string metodoRecurso, string? payloadHash, TimeSpan ttl, CancellationToken ct = default)
    {
        var entry = IdempotencyKey.CriarPendente(key, empresaId, metodoRecurso, payloadHash, ttl);
        try
        {
            // issue 917 Fase B: INSERT-first — o ponto de serializacao e o proprio INSERT
            // (via ux_idempotency_key_empresa_recurso), nao mais um GetActiveAsync ANTES do
            // SaveAsync do vencedor. Concorrentes com a MESMA chave: um vence este INSERT, o
            // outro cai no catch abaixo com a linha do vencedor.
            await dbContext.IdempotencyKeys.AddAsync(entry, ct);
            await dbContext.SaveChangesAsync(ct);
            return (entry, true);
        }
        catch (DbUpdateException)
        {
            dbContext.Entry(entry).State = EntityState.Detached;
            var agora = DateTime.UtcNow;
            var existing = await dbContext.IdempotencyKeys
                .AsNoTracking()
                .FirstOrDefaultAsync(x =>
                    x.Key == key && x.EmpresaId == empresaId && x.MetodoRecurso == metodoRecurso && x.ExpiraEm > agora,
                    ct);
            if (existing is null)
                throw; // corrida rara: colidiu mas a linha ja expirou/foi limpa -- propaga, o middleware decide.
            return (existing, false);
        }
    }

    public async Task<bool> TryTakeoverAsync(Guid id, DateTime lockedAtUtcEsperado, DateTime novoLock, CancellationToken ct = default)
    {
        // ExecuteUpdateAsync compila pra UPDATE ... WHERE ... direto no SQL -- atomico,
        // nao passa pelo ChangeTracker. E o mesmo padrao de CAS que TryReabrirAsync usa.
        var linhas = await dbContext.IdempotencyKeys
            .Where(x => x.Id == id && x.Status == StatusIdempotencyKey.Pendente && x.LockedAtUtc == lockedAtUtcEsperado)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.LockedAtUtc, novoLock), ct);
        return linhas == 1;
    }

    public async Task<bool> TryReabrirAsync(Guid id, string? novoPayloadHash, DateTime novoLock, CancellationToken ct = default)
    {
        var linhas = await dbContext.IdempotencyKeys
            .Where(x => x.Id == id && x.Status == StatusIdempotencyKey.Falhou)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.Status, StatusIdempotencyKey.Pendente)
                .SetProperty(x => x.PayloadHash, novoPayloadHash)
                .SetProperty(x => x.LockedAtUtc, novoLock), ct);
        return linhas == 1;
    }

    public Task MarcarConcluidoAsync(Guid id, int httpStatus, string? respostaJson, CancellationToken ct = default) =>
        dbContext.IdempotencyKeys
            .Where(x => x.Id == id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.Status, StatusIdempotencyKey.Concluido)
                .SetProperty(x => x.HttpStatus, httpStatus)
                .SetProperty(x => x.RespostaJson, respostaJson)
                .SetProperty(x => x.LockedAtUtc, DateTime.UtcNow), ct);

    public Task MarcarFalhouAsync(Guid id, CancellationToken ct = default) =>
        dbContext.IdempotencyKeys
            .Where(x => x.Id == id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.Status, StatusIdempotencyKey.Falhou)
                .SetProperty(x => x.LockedAtUtc, DateTime.UtcNow), ct);

    public Task<int> CleanupExpiredAsync(DateTime referencia, CancellationToken ct = default) =>
        dbContext.IdempotencyKeys
            .Where(x => x.ExpiraEm <= referencia)
            .ExecuteDeleteAsync(ct);
}
