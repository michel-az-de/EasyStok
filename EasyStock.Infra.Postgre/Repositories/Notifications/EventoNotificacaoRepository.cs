using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Domain.Entities.Notifications;
using EasyStock.Domain.Enums.Notifications;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Infra.Postgre.Repositories.Notifications;

public sealed class EventoNotificacaoRepository(EasyStockDbContext db) : IEventoNotificacaoRepository
{
    public Task<EventoNotificacao?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        db.NotifEventos.FirstOrDefaultAsync(e => e.Id == id, ct);

    public async Task<IReadOnlyList<EventoNotificacao>> ListarPendentesAsync(
        int limit = 100, CancellationToken ct = default)
    {
        return await db.NotifEventos.AsNoTracking()
            .Where(e => e.Status == StatusEventoNotificacao.Pendente)
            .OrderBy(e => e.OcorridoEm)
            .Take(limit)
            .ToListAsync(ct);
    }

    public async Task AddAsync(EventoNotificacao evento, CancellationToken ct = default) =>
        await db.NotifEventos.AddAsync(evento, ct);

    public Task UpdateAsync(EventoNotificacao evento, CancellationToken ct = default)
    {
        db.NotifEventos.Update(evento);
        return Task.CompletedTask;
    }
}
