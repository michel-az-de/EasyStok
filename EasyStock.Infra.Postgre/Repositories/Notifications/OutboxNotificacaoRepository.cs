using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Domain.Entities.Notifications;
using EasyStock.Domain.Enums.Notifications;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Infra.Postgre.Repositories.Notifications;

public sealed class OutboxNotificacaoRepository(EasyStockDbContext db) : IOutboxNotificacaoRepository
{
    public Task<OutboxMensagemNotificacao?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        db.NotifOutboxMensagens.FirstOrDefaultAsync(m => m.Id == id, ct);

    // SQL cru de propósito: FOR UPDATE SKIP LOCKED não sai do LINQ (padrão da S39, MensagemProgramadaRepository).
    // IgnoreQueryFilters mantém o SQL sem composição (o filtro global de tenant o embrulharia): o claim é
    // cross-tenant e roda com o bypass de RLS ligado pelo dispatcher, pela porta IRowLevelSecurityBypass.
    public async Task<IReadOnlyList<OutboxMensagemNotificacao>> ReservarParaEnvioAsync(
        int limite, CancellationToken ct = default)
    {
        var agora = DateTime.UtcNow;
        var pendente = nameof(StatusOutbox.Pendente);
        var reservadas = await db.NotifOutboxMensagens
            .FromSqlInterpolated($"""
                SELECT * FROM notif_outbox_mensagens
                WHERE "Status" = {pendente} AND "ProximaTentativaEm" <= {agora}
                ORDER BY "ProximaTentativaEm"
                LIMIT {limite}
                FOR UPDATE SKIP LOCKED
                """)
            .IgnoreQueryFilters()
            .ToListAsync(ct);

        foreach (var mensagem in reservadas)
            mensagem.MarcarEmEnvio();
        return reservadas;
    }

    public Task<OutboxMensagemNotificacao?> ObterAsync(Guid empresaId, Guid id, CancellationToken ct = default) =>
        db.NotifOutboxMensagens.IgnoreQueryFilters().FirstOrDefaultAsync(m => m.EmpresaId == empresaId && m.Id == id, ct);

    public Task<bool> ExisteAsync(string idempotencyKey, CancellationToken ct = default) =>
        db.NotifOutboxMensagens.AnyAsync(m => m.IdempotencyKey == idempotencyKey, ct);

    public Task<bool> ExisteMensagemAbertaDoEventoAsync(
        Guid empresaId, Guid eventoId, Guid exceto, CancellationToken ct = default) =>
        db.NotifOutboxMensagens.AnyAsync(m => m.EmpresaId == empresaId
                                              && m.EventoId == eventoId
                                              && m.Id != exceto
                                              && (m.Status == StatusOutbox.Pendente || m.Status == StatusOutbox.EmEnvio), ct);

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
