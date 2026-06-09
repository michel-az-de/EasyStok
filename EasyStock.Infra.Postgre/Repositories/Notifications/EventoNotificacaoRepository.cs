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
        // ADR-0030: no caminho de PublicarEventoAsync o evento foi recem-AddAsync (Added) e ja
        // carrega o Status final (MarcarComoProcessado/Falhado mutam in-place ANTES deste Update).
        // Chamar Update o rebaixaria a Modified -> UPDATE de 0 linhas (row inexistente) ->
        // DbUpdateConcurrencyException, abortando o commit e matando TODA notificacao (helpdesk +
        // jobs). So fazemos Update quando Detached (caminho do avaliador, que le via AsNoTracking);
        // Added/Modified ja persistem o estado corrente no proximo SaveChanges.
        if (db.Entry(evento).State == EntityState.Detached)
            db.NotifEventos.Update(evento);
        return Task.CompletedTask;
    }
}
