using System.Diagnostics.Metrics;
using System.Text.Json;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Domain.Enums.Notifications;
using EasyStock.Infra.Postgre.Concurrency;
using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EasyStock.Worker.BackgroundServices;

/// <summary>
/// Monitor de SLA: a cada 5 minutos, varre tickets nao resolvidos e dispara eventos
/// SlaProximoVencer (50% e 80% do prazo) e SlaViolado (prazo estourado).
/// Deduplicacao via UltimoAlerta50PctEm/UltimoAlerta80PctEm + flags SlaResposta/ResolucaoViolado.
/// Usa advisory lock para single-instance entre replicas do worker.
/// </summary>
public sealed class SlaMonitorService(
    IServiceProvider serviceProvider,
    IOptions<WorkerOptions> options,
    ILogger<SlaMonitorService> logger) : BackgroundService
{
    private static readonly Meter HelpdeskMeter = new("EasyStock.Helpdesk", "1.0");
    private static readonly Counter<long> SlaResponseBreached = HelpdeskMeter.CreateCounter<long>("tickets.sla_response_breached", "tickets");
    private static readonly Counter<long> SlaResolutionBreached = HelpdeskMeter.CreateCounter<long>("tickets.sla_resolution_breached", "tickets");
    private static readonly Counter<long> SlaProximoCounter = HelpdeskMeter.CreateCounter<long>("tickets.sla_proximo_alerta", "tickets");

    private static readonly TicketStatus[] StatusAtivos = [TicketStatus.Aberto, TicketStatus.EmAtendimento, TicketStatus.AguardandoCliente];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("SlaMonitorService iniciado");

        var intervaloSegundos = Math.Max(60, options.Value.SlaMonitorIntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ExecutarTickAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Erro durante tick do SlaMonitorService");
            }

            try { await Task.Delay(TimeSpan.FromSeconds(intervaloSegundos), stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task ExecutarTickAsync(CancellationToken ct)
    {
        using var scope = serviceProvider.CreateScope();
        var sp = scope.ServiceProvider;
        var advisoryLock = sp.GetRequiredService<PostgresAdvisoryLock>();

        await advisoryLock.TentarExecutarAsync(LockKeys.SlaMonitor, async token =>
        {
            ct = token;
            var db = sp.GetRequiredService<EasyStockDbContext>();
            // Cross-tenant: monitor SLA varre AdminTickets de TODAS as empresas. Sem
            // usuario autenticado, sem bypass o RLS (app.bypass_rls) bloqueia leitura
            // E o filtro EF zera o IQueryable. Defesa em profundidade exige os dois layers.
            db.BypassRowLevelSecurity = true;
            var notificador = sp.GetRequiredService<INotificadorService>();

            var agora = DateTime.UtcNow;

            // Carregar candidatos: tickets ativos com prazo de resposta OU resolucao definido
            // e que ainda nao tem todas as flags de violado true.
            var candidatos = await db.AdminTickets
                .IgnoreQueryFilters() // cross-tenant: monitor SLA de todas as empresas
                .AsNoTracking()
                .Where(t => StatusAtivos.Contains(t.Status))
                .Where(t => t.PrazoResposta != null || t.PrazoResolucao != null)
                .Where(t => !t.SlaRespostaViolado || !t.SlaResolucaoViolado || t.UltimoAlerta80PctEm == null)
                .Select(t => new TicketSlaSnapshot(
                    t.Id, t.EmpresaId, t.Titulo, t.Prioridade, t.Nivel,
                    t.AtendenteId, t.CriadoEm,
                    t.PrazoResposta, t.PrazoResolucao,
                    t.PrimeiraRespostaEm, t.ResolvidoEm,
                    t.SlaRespostaViolado, t.SlaResolucaoViolado,
                    t.UltimoAlerta50PctEm, t.UltimoAlerta80PctEm))
                .ToListAsync(token);

            foreach (var snap in candidatos)
            {
                await ProcessarTicketAsync(snap, agora, db, notificador, token);
            }

            await db.CommitAsync();
        }, ct);
    }

    private async Task ProcessarTicketAsync(
        TicketSlaSnapshot snap,
        DateTime agora,
        EasyStockDbContext db,
        INotificadorService notificador,
        CancellationToken ct)
    {
        // ----- SLA de Resposta -----
        if (snap.PrazoResposta is { } prazoResp && snap.PrimeiraRespostaEm is null)
        {
            await AvaliarPrazoAsync("Resposta", snap, prazoResp, snap.SlaRespostaViolado, agora, db, notificador, ct);
        }

        // ----- SLA de Resolucao -----
        if (snap.PrazoResolucao is { } prazoResol && snap.ResolvidoEm is null)
        {
            await AvaliarPrazoAsync("Resolucao", snap, prazoResol, snap.SlaResolucaoViolado, agora, db, notificador, ct);
        }
    }

    private async Task AvaliarPrazoAsync(
        string tipo,
        TicketSlaSnapshot snap,
        DateTime prazo,
        bool jaViolado,
        DateTime agora,
        EasyStockDbContext db,
        INotificadorService notificador,
        CancellationToken ct)
    {
        if (jaViolado) return;

        if (agora >= prazo)
        {
            // Estourou — marcar violado + notificar
            if (tipo == "Resposta")
                await db.AdminTickets.Where(t => t.Id == snap.Id)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(t => t.SlaRespostaViolado, true)
                        .SetProperty(t => t.AlteradoEm, agora), ct);
            else
                await db.AdminTickets.Where(t => t.Id == snap.Id)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(t => t.SlaResolucaoViolado, true)
                        .SetProperty(t => t.AlteradoEm, agora), ct);

            db.TicketHistoricos.Add(TicketHistorico.Criar(
                snap.Id, autorId: null, TicketAcaoHistorico.SlaViolado,
                valorDepois: tipo));

            if (tipo == "Resposta") SlaResponseBreached.Add(1, new KeyValuePair<string, object?>("prioridade", snap.Prioridade.ToString()));
            else SlaResolutionBreached.Add(1, new KeyValuePair<string, object?>("prioridade", snap.Prioridade.ToString()));

            await notificador.PublicarEventoAsync(
                TipoEventoNotificacao.SlaViolado,
                snap.EmpresaId,
                usuarioDestinoId: snap.AtendenteId,
                payloadJson: JsonSerializer.Serialize(new
                {
                    ticketId = snap.Id,
                    titulo = snap.Titulo,
                    tipoSla = tipo,
                    prioridade = snap.Prioridade.ToString(),
                    nivel = snap.Nivel.ToString(),
                    empresaNome = ""
                }),
                ct: ct);
            return;
        }

        // Nao violado — checar 50% e 80%
        var minutosTotais = (prazo - snap.CriadoEm).TotalMinutes;
        if (minutosTotais <= 0) return;

        var minutosDecorridos = (agora - snap.CriadoEm).TotalMinutes;
        var pctDecorrido = minutosDecorridos / minutosTotais;
        var minutosRestantes = (int)Math.Ceiling((prazo - agora).TotalMinutes);

        if (pctDecorrido >= 0.80 && snap.UltimoAlerta80PctEm is null)
        {
            await DispararAlertaProximoAsync(snap, tipo, 80, minutosRestantes, agora, db, notificador, ct);
            await db.AdminTickets.Where(t => t.Id == snap.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.UltimoAlerta80PctEm, agora), ct);
        }
        else if (pctDecorrido >= 0.50 && snap.UltimoAlerta50PctEm is null)
        {
            await DispararAlertaProximoAsync(snap, tipo, 50, minutosRestantes, agora, db, notificador, ct);
            await db.AdminTickets.Where(t => t.Id == snap.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.UltimoAlerta50PctEm, agora), ct);
        }
    }

    private async Task DispararAlertaProximoAsync(
        TicketSlaSnapshot snap, string tipo, int percentual, int minutosRestantes,
        DateTime agora, EasyStockDbContext db, INotificadorService notificador, CancellationToken ct)
    {
        SlaProximoCounter.Add(1,
            new KeyValuePair<string, object?>("prioridade", snap.Prioridade.ToString()),
            new KeyValuePair<string, object?>("percentual", percentual));

        db.TicketHistoricos.Add(TicketHistorico.Criar(
            snap.Id, autorId: null, TicketAcaoHistorico.SlaProximoVencer,
            valorDepois: $"{tipo}:{percentual}%"));

        await notificador.PublicarEventoAsync(
            TipoEventoNotificacao.SlaProximoVencer,
            snap.EmpresaId,
            usuarioDestinoId: snap.AtendenteId,
            payloadJson: JsonSerializer.Serialize(new
            {
                ticketId = snap.Id,
                titulo = snap.Titulo,
                tipoSla = tipo,
                percentual,
                minutosRestantes,
                prioridade = snap.Prioridade.ToString(),
                nivel = snap.Nivel.ToString()
            }),
            ct: ct);
    }

    private sealed record TicketSlaSnapshot(
        Guid Id, Guid EmpresaId, string Titulo,
        TicketPrioridade Prioridade, NivelAtendimento Nivel,
        Guid? AtendenteId, DateTime CriadoEm,
        DateTime? PrazoResposta, DateTime? PrazoResolucao,
        DateTime? PrimeiraRespostaEm, DateTime? ResolvidoEm,
        bool SlaRespostaViolado, bool SlaResolucaoViolado,
        DateTime? UltimoAlerta50PctEm, DateTime? UltimoAlerta80PctEm);
}
