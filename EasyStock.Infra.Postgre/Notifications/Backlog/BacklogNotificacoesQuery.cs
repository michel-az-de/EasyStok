using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Application.Ports.Output.Security;
using EasyStock.Domain.Enums.Notifications;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Infra.Postgre.Notifications.Backlog;

/// <summary>
/// Implementa <see cref="IBacklogNotificacoes"/> (N1): consultas agregadas (<c>MIN</c>, <c>COUNT</c>, <c>GROUP BY</c>)
/// sobre o outbox e os eventos de todas as empresas, sem trazer linha. O bypass de RLS entra pela porta
/// <see cref="IRowLevelSecurityBypass"/> antes da primeira conexão: sem ele a policy <c>tenant_isolation</c> devolve zero
/// e o backlog parece vazio justamente quando o motor está parado.
/// </summary>
public sealed class BacklogNotificacoesQuery(EasyStockDbContext db, IRowLevelSecurityBypass bypassRls) : IBacklogNotificacoes
{
    public async Task<BacklogNotificacoes> MedirAsync(CancellationToken ct = default)
    {
        using var _ = bypassRls.Begin();
        var agora = DateTime.UtcNow;
        var haUmaHora = agora.AddHours(-1);
        var ha24Horas = agora.AddHours(-24);

        var outbox = db.NotifOutboxMensagens.IgnoreQueryFilters().AsNoTracking();
        var eventos = db.NotifEventos.IgnoreQueryFilters().AsNoTracking();

        var pendenteMaisAntigo = await outbox
            .Where(m => m.Status == StatusOutbox.Pendente && m.ProximaTentativaEm <= agora)
            .MinAsync(m => (DateTime?)m.ProximaTentativaEm, ct);
        var eventoMaisAntigo = await eventos
            .Where(e => e.Status == StatusEventoNotificacao.Pendente)
            .MinAsync(e => (DateTime?)e.OcorridoEm, ct);
        var emEnvioAlemDoLease = await outbox
            .CountAsync(m => m.Status == StatusOutbox.EmEnvio && m.ProximaTentativaEm < agora, ct);

        // Estados terminais: ProximaTentativaEm guarda o momento em que a mensagem terminou.
        var terminadasNaHora = await outbox
            .Where(m => m.ProximaTentativaEm >= haUmaHora
                        && (m.Status == StatusOutbox.Falhado || m.Status == StatusOutbox.Simulado
                            || m.Status == StatusOutbox.Indeterminado || m.Status == StatusOutbox.Expirado))
            .GroupBy(m => m.Status)
            .Select(g => new { Status = g.Key, Total = g.Count() })
            .ToListAsync(ct);
        int Total(StatusOutbox status) => terminadasNaHora.FirstOrDefault(t => t.Status == status)?.Total ?? 0;

        var processadosSemOutbox = await eventos
            .Where(e => e.Status == StatusEventoNotificacao.Processado && e.ProcessadoEm >= ha24Horas)
            .CountAsync(e => !db.NotifOutboxMensagens.IgnoreQueryFilters().Any(m => m.EventoId == e.Id), ct);

        return new BacklogNotificacoes(
            pendenteMaisAntigo is { } p ? agora - p : null,
            eventoMaisAntigo is { } e0 ? agora - e0 : null,
            emEnvioAlemDoLease,
            Total(StatusOutbox.Falhado),
            Total(StatusOutbox.Simulado),
            Total(StatusOutbox.Indeterminado),
            Total(StatusOutbox.Expirado),
            processadosSemOutbox);
    }
}
