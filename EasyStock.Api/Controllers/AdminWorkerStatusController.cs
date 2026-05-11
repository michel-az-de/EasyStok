using EasyStock.Api.Http;
using EasyStock.Domain.Enums;
using EasyStock.Domain.Enums.Notifications;
using EasyStock.Domain.Integration;
using EasyStock.Infra.Postgre.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Api.Controllers;

/// <summary>
/// Dashboard de saude do EasyStock.Worker. Consumido pelo Admin/Operacao/Worker
/// via AdminApiClient. Cruza:
/// - Heartbeats dos 7 hosted services (tabela worker_heartbeats, UPSERT por Servico).
/// - Pipeline de notificacoes (NotifOutboxMensagens / NotifLogsEnvio nas 24h).
/// - Helpdesk SLA (AdminTickets violados/abertos).
/// - Outbox de integracao externa (OutboxEventosIntegracao).
///
/// SuperAdmin only — leitura cross-tenant via IgnoreQueryFilters().
/// ResponseCache 10s pra suavizar polling do dashboard (auto-refresh 30s = 3x cache hit).
/// </summary>
[ApiController]
[Route("api/admin/worker-status")]
[Authorize(Policy = "SuperAdmin")]
[ResponseCache(Duration = 10)]
public class AdminWorkerStatusController(EasyStockDbContext db) : EasyStockControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var agora = DateTime.UtcNow;
        var cutoff24h = agora.AddHours(-24);

        // ── Heartbeats ──
        var heartbeatsRaw = await db.WorkerHeartbeats
            .AsNoTracking()
            .IgnoreQueryFilters()
            .OrderBy(h => h.Servico)
            .ToListAsync(ct);

        var heartbeats = heartbeatsRaw.Select(h => new
        {
            servico = h.Servico,
            ultimoTickEm = h.UltimoTickEm,
            segundosDesdeUltimoTick = (int)Math.Max(0, (agora - h.UltimoTickEm).TotalSeconds),
            status = h.Status,
            detalhe = h.Detalhe,
            itensProcessados = h.ItensProcessados,
            duracaoMs = h.DuracaoMs,
            alteradoEm = h.AlteradoEm,
        }).ToList();

        // ── Notifications pipeline ──
        var notifEnviadas24h = await db.NotifOutboxMensagens
            .IgnoreQueryFilters()
            .CountAsync(m => m.Status == StatusOutbox.Enviado && m.EnviadoEm >= cutoff24h, ct);

        var notifFalhadas24h = await db.NotifOutboxMensagens
            .IgnoreQueryFilters()
            .CountAsync(m => m.Status == StatusOutbox.Falhado && m.CriadoEm >= cutoff24h, ct);

        var notifPendentes = await db.NotifOutboxMensagens
            .IgnoreQueryFilters()
            .CountAsync(m => m.Status == StatusOutbox.Pendente, ct);

        DateTime? oldestPending = await db.NotifOutboxMensagens
            .IgnoreQueryFilters()
            .Where(m => m.Status == StatusOutbox.Pendente)
            .OrderBy(m => m.CriadoEm)
            .Select(m => (DateTime?)m.CriadoEm)
            .FirstOrDefaultAsync(ct);

        var notifLagSegundos = oldestPending is null
            ? 0
            : (int)Math.Max(0, (agora - oldestPending.Value).TotalSeconds);

        var porCanal = await db.NotifOutboxMensagens
            .IgnoreQueryFilters()
            .Where(m => m.CriadoEm >= cutoff24h)
            .GroupBy(m => m.Canal)
            .Select(g => new
            {
                canal = g.Key.ToString(),
                enviadas = g.Count(m => m.Status == StatusOutbox.Enviado),
                falhadas = g.Count(m => m.Status == StatusOutbox.Falhado),
                pendentes = g.Count(m => m.Status == StatusOutbox.Pendente),
            })
            .ToListAsync(ct);

        // ── Helpdesk SLA ──
        var statusAtivos = new[] { TicketStatus.Aberto, TicketStatus.EmAtendimento, TicketStatus.AguardandoCliente };
        var ticketsAbertos = await db.AdminTickets
            .IgnoreQueryFilters()
            .CountAsync(t => statusAtivos.Contains(t.Status), ct);

        var slasViolados24h = await db.AdminTickets
            .IgnoreQueryFilters()
            .CountAsync(t => (t.SlaRespostaViolado || t.SlaResolucaoViolado)
                          && t.AlteradoEm >= cutoff24h, ct);

        var slasViolacaoAtiva = await db.AdminTickets
            .IgnoreQueryFilters()
            .CountAsync(t => statusAtivos.Contains(t.Status)
                          && (t.SlaRespostaViolado || t.SlaResolucaoViolado), ct);

        var slasProximosVencer = await db.AdminTickets
            .IgnoreQueryFilters()
            .CountAsync(t => statusAtivos.Contains(t.Status)
                          && t.UltimoAlerta80PctEm != null
                          && !t.SlaRespostaViolado
                          && !t.SlaResolucaoViolado, ct);

        // ── Integration Outbox ──
        var integPendentes = await db.OutboxEventosIntegracao
            .IgnoreQueryFilters()
            .CountAsync(o => o.Status == StatusOutboxIntegracao.Pendente, ct);

        var integEmEnvio = await db.OutboxEventosIntegracao
            .IgnoreQueryFilters()
            .CountAsync(o => o.Status == StatusOutboxIntegracao.EmEnvio, ct);

        var integEnviados24h = await db.OutboxEventosIntegracao
            .IgnoreQueryFilters()
            .CountAsync(o => o.Status == StatusOutboxIntegracao.Enviado
                          && o.ProcessadoEm >= cutoff24h, ct);

        var integFalhados = await db.OutboxEventosIntegracao
            .IgnoreQueryFilters()
            .CountAsync(o => o.Status == StatusOutboxIntegracao.Falhado, ct);

        // ── Health derivado: nenhum heartbeat, ou todos heartbeats com erro/skip ──
        var worstHeartbeat = heartbeatsRaw.Count == 0
            ? "sem-dados"
            : heartbeatsRaw.Any(h => h.Status == "Erro")
                ? "erro"
                : heartbeatsRaw.All(h => h.Status == "Skip")
                    ? "skip"
                    : "ok";

        // Worker saudavel se algum hosted service teve tick nos ultimos 5 minutos.
        var algumTickRecente = heartbeatsRaw.Any(h => (agora - h.UltimoTickEm).TotalMinutes < 5);

        return DataOk(new
        {
            agoraUtc = agora,
            saude = new
            {
                workerSaudavel = heartbeatsRaw.Count > 0 && algumTickRecente && worstHeartbeat == "ok",
                worstHeartbeat,
                ultimaAtividadeHa = heartbeatsRaw.Count == 0
                    ? (int?)null
                    : (int)heartbeatsRaw.Min(h => (agora - h.UltimoTickEm).TotalSeconds),
            },
            heartbeats,
            notifications = new
            {
                enviadas24h = notifEnviadas24h,
                falhadas24h = notifFalhadas24h,
                pendentes = notifPendentes,
                lagSegundos = notifLagSegundos,
                porCanal,
            },
            helpdesk = new
            {
                ticketsAbertos,
                slasViolados24h,
                slasViolacaoAtiva,
                slasProximosVencer,
            },
            integrationOutbox = new
            {
                pendentes = integPendentes,
                emEnvio = integEmEnvio,
                enviados24h = integEnviados24h,
                falhados = integFalhados,
            },
        });
    }
}
