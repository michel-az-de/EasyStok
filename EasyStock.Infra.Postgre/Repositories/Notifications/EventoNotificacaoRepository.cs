using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Application.Services.Notifications;
using EasyStock.Domain.Entities.Notifications;
using EasyStock.Domain.Enums.Notifications;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Infra.Postgre.Repositories.Notifications;

public sealed class EventoNotificacaoRepository(EasyStockDbContext db) : IEventoNotificacaoRepository
{
    public Task<EventoNotificacao?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        db.NotifEventos.FirstOrDefaultAsync(e => e.Id == id, ct);

    public Task<EventoNotificacao?> ObterAsync(Guid empresaId, Guid id, CancellationToken ct = default) =>
        db.NotifEventos.IgnoreQueryFilters().FirstOrDefaultAsync(e => e.EmpresaId == empresaId && e.Id == id, ct);

    public async Task<IReadOnlyList<EventoPendente>> ListarPendentesParaAvaliarAsync(
        int limit = 100, CancellationToken ct = default)
    {
        return await db.NotifEventos.IgnoreQueryFilters().AsNoTracking()
            .Where(e => e.Status == StatusEventoNotificacao.Pendente)
            .OrderBy(e => e.OcorridoEm)
            .Take(limit)
            .Select(e => new EventoPendente(e.Id, e.EmpresaId))
            .ToListAsync(ct);
    }

    public async Task<int> ExpirarPendentesAsync(
        PoliticaValidadeNotificacao politica, int limitePorPrazo, CancellationToken ct = default)
    {
        var agora = DateTime.UtcNow;
        var total = 0;
        // Um grupo por prazo (poucos): o prazo vai no WHERE, em vez de trazer o backlog inteiro para decidir tipo a tipo.
        foreach (var (prazo, tipos) in politica.PorPrazo())
        {
            var limite = agora - prazo;
            var tiposDoGrupo = tipos.ToArray();
            var vencidos = await db.NotifEventos.IgnoreQueryFilters()
                .Where(e => e.Status == StatusEventoNotificacao.Pendente
                            && e.OcorridoEm < limite
                            && tiposDoGrupo.Contains(e.Tipo))
                .OrderBy(e => e.OcorridoEm)
                .Take(limitePorPrazo)
                .ToListAsync(ct);

            foreach (var evento in vencidos)
                evento.MarcarComoExpirado(
                    $"Expirado: passou do prazo de {prazo.TotalMinutes:0} min sem ser avaliado",
                    purgarPayload: PoliticaValidadeNotificacao.CarregaSegredo(evento.Tipo));
            total += vencidos.Count;
        }

        return total;
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
