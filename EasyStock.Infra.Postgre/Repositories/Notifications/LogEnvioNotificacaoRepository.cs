using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Domain.Entities.Notifications;
using EasyStock.Domain.Enums.Notifications;
using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Infra.Postgre.Repositories.Notifications;

public sealed class LogEnvioNotificacaoRepository(EasyStockDbContext db) : ILogEnvioNotificacaoRepository
{
    public async Task AddAsync(LogEnvioNotificacao log, CancellationToken ct = default) =>
        await db.NotifLogsEnvio.AddAsync(log, ct);

    public async Task<IReadOnlyList<LogEnvioNotificacao>> ListarPorOutboxAsync(
        Guid outboxMensagemId, CancellationToken ct = default) =>
        await db.NotifLogsEnvio.AsNoTracking()
            .Where(l => l.OutboxMensagemId == outboxMensagemId)
            .OrderBy(l => l.OcorridoEm)
            .ToListAsync(ct);

    public async Task<(IReadOnlyList<LogEnvioNotificacao> Items, int TotalCount)> ListarAsync(
        Guid? empresaId, StatusOutbox? status = null, CanalNotificacao? canal = null,
        DateTime? de = null, DateTime? ate = null,
        int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        var q = db.NotifLogsEnvio.AsNoTracking()
            .Join(db.NotifOutboxMensagens, l => l.OutboxMensagemId, m => m.Id,
                (l, m) => new { Log = l, Mensagem = m })
            .Where(x => empresaId == null || x.Mensagem.EmpresaId == empresaId);

        if (status.HasValue)
        {
            // Status fica no OutboxMensagem, não no Log — filtra pela mensagem associada
            q = q.Where(x => x.Mensagem.Status == status);
        }
        if (canal.HasValue) q = q.Where(x => x.Log.Canal == canal);
        if (de.HasValue) q = q.Where(x => x.Log.OcorridoEm >= de);
        if (ate.HasValue) q = q.Where(x => x.Log.OcorridoEm <= ate);

        var total = await q.CountAsync(ct);
        var items = await q
            .OrderByDescending(x => x.Log.OcorridoEm)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => x.Log)
            .ToListAsync(ct);

        return ((IReadOnlyList<LogEnvioNotificacao>)items, total);
    }
}
