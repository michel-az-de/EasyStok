using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Domain.Entities.Notifications;
using EasyStock.Domain.Enums.Notifications;
using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Infra.Postgre.Repositories.Notifications;

public sealed class RotinaNotificacaoRepository(EasyStockDbContext db) : IRotinaRepository
{
    public Task<RotinaNotificacao?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        db.NotifRotinas.FirstOrDefaultAsync(r => r.Id == id, ct);

    public Task<RotinaNotificacao?> GetByCodigoAsync(string codigo, Guid? empresaId, CancellationToken ct = default) =>
        db.NotifRotinas.FirstOrDefaultAsync(
            r => r.Codigo == codigo && r.EmpresaId == empresaId, ct);

    public async Task<IReadOnlyList<RotinaNotificacao>> ListarAtivasAsync(
        TipoEventoNotificacao? tipoEvento = null, CancellationToken ct = default)
    {
        var q = db.NotifRotinas.AsNoTracking().Where(r => r.Ativa);
        if (tipoEvento.HasValue) q = q.Where(r => r.TipoEvento == tipoEvento);
        return await q.ToListAsync(ct);
    }

    public async Task<IReadOnlyList<RotinaNotificacao>> ListarAsync(
        Guid? empresaId, bool? ativa = null, int page = 1, int pageSize = 20,
        CancellationToken ct = default)
    {
        var q = db.NotifRotinas.AsNoTracking()
            .Where(r => r.EmpresaId == empresaId);
        if (ativa.HasValue) q = q.Where(r => r.Ativa == ativa);
        return await q.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
    }

    public async Task AddAsync(RotinaNotificacao rotina, CancellationToken ct = default) =>
        await db.NotifRotinas.AddAsync(rotina, ct);

    public Task UpdateAsync(RotinaNotificacao rotina, CancellationToken ct = default)
    {
        db.NotifRotinas.Update(rotina);
        return Task.CompletedTask;
    }
}
