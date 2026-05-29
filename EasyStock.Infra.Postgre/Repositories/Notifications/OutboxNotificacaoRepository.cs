using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Domain.Entities.Notifications;
using EasyStock.Domain.Enums.Notifications;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Infra.Postgre.Repositories.Notifications;

public sealed class OutboxNotificacaoRepository(EasyStockDbContext db) : IOutboxNotificacaoRepository
{
    public Task<OutboxMensagemNotificacao?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        db.NotifOutboxMensagens.FirstOrDefaultAsync(m => m.Id == id, ct);

    public async Task<IReadOnlyList<OutboxMensagemNotificacao>> ListarPendentesParaProcessarAsync(
        int shardKey, int batchSize, CancellationToken ct = default)
    {
        return await db.NotifOutboxMensagens
            .Where(m => m.ShardKey == shardKey
                        && m.Status == StatusOutbox.Pendente
                        && m.ProximaTentativaEm <= DateTime.UtcNow)
            .OrderBy(m => m.ProximaTentativaEm)
            .Take(batchSize)
            .ToListAsync(ct);
    }

    public Task<bool> ExisteAsync(string idempotencyKey, CancellationToken ct = default) =>
        db.NotifOutboxMensagens.AnyAsync(m => m.IdempotencyKey == idempotencyKey, ct);

    public async Task<(IReadOnlyList<OutboxMensagemNotificacao> Items, int TotalCount)> ListarAsync(
        Guid? empresaId, StatusOutbox? status = null, CanalNotificacao? canal = null,
        DateTime? de = null, DateTime? ate = null,
        int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        var q = db.NotifOutboxMensagens.AsNoTracking()
            .Where(m => empresaId == null || m.EmpresaId == empresaId);

        if (status.HasValue) q = q.Where(m => m.Status == status);
        if (canal.HasValue) q = q.Where(m => m.Canal == canal);
        if (de.HasValue) q = q.Where(m => m.CriadoEm >= de);
        if (ate.HasValue) q = q.Where(m => m.CriadoEm <= ate);

        var total = await q.CountAsync(ct);
        var items = await q.OrderByDescending(m => m.CriadoEm)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);

        return ((IReadOnlyList<OutboxMensagemNotificacao>)items, total);
    }

    public async Task AddAsync(OutboxMensagemNotificacao mensagem, CancellationToken ct = default) =>
        await db.NotifOutboxMensagens.AddAsync(mensagem, ct);

    public async Task AddRangeAsync(IEnumerable<OutboxMensagemNotificacao> mensagens, CancellationToken ct = default) =>
        await db.NotifOutboxMensagens.AddRangeAsync(mensagens, ct);

    public Task UpdateAsync(OutboxMensagemNotificacao mensagem, CancellationToken ct = default)
    {
        db.NotifOutboxMensagens.Update(mensagem);
        return Task.CompletedTask;
    }
}
