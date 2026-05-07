using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Domain.Entities.Notifications;
using EasyStock.Domain.Enums.Notifications;
using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Infra.Postgre.Repositories.Notifications;

public sealed class BloqueioNotificacaoRepository(EasyStockDbContext db) : IBloqueioNotificacaoRepository
{
    public Task<BloqueioNotificacao?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        db.NotifBloqueios.FirstOrDefaultAsync(b => b.Id == id, ct);

    public async Task<IReadOnlyList<BloqueioNotificacao>> ListarAtivosAsync(
        Guid? empresaId, CanalNotificacao? canal = null, CancellationToken ct = default)
    {
        var agora = DateTime.UtcNow;
        var q = db.NotifBloqueios.AsNoTracking()
            .Where(b => b.RemovidoEm == null && (b.ExpiraEm == null || b.ExpiraEm > agora));

        // Inclui bloqueios globais (EmpresaId=null) + específicos da empresa
        q = q.Where(b => b.EmpresaId == null || b.EmpresaId == empresaId);

        if (canal.HasValue)
            q = q.Where(b => b.Canal == null || b.Canal == canal);

        return await q.ToListAsync(ct);
    }

    public async Task AddAsync(BloqueioNotificacao bloqueio, CancellationToken ct = default) =>
        await db.NotifBloqueios.AddAsync(bloqueio, ct);

    public Task UpdateAsync(BloqueioNotificacao bloqueio, CancellationToken ct = default)
    {
        db.NotifBloqueios.Update(bloqueio);
        return Task.CompletedTask;
    }
}
