using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text.Json;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Notifications;
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

    // 0x534C4148 = "SLAH" (SLA Helpdesk) - lock unico para single-instance
    private const long LockId = 0x534C_4148_0000_0001L;

    private static readonly TicketStatus[] StatusAtivos = [TicketStatus.Aberto, TicketStatus.EmAtendimento, TicketStatus.AguardandoCliente];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("SlaMonitorService iniciado");

        var intervaloSegundos = Math.Max(60, options.Value.SlaMonitorIntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            var sw = Stopwatch.StartNew();
            int processados = 0;
            string status = "OK";
            string? detalhe = null;

            try
            {
                processados = await ExecutarTickAsync(stoppingToken);
                if (processados < 0)
                {
                    status = "Skip";
                    detalhe = "advisory lock detido por outra replica";
                    processados = 0;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Shutdown limpo — nao gravar heartbeat "Erro" enganoso (deploy/restart).
                break;
            }
            catch (Exception ex)
            {
                status = "Erro";
                detalhe = ex.GetType().Name + ": " + ex.Message;
                logger.LogError(ex, "Erro durante tick do SlaMonitorService");
            }
            finally
            {
                sw.Stop();
                if (!stoppingToken.IsCancellationRequested)
                {
                    await GravarHeartbeatAsync("SlaMonitor", status, detalhe,
                        processados, (int)sw.ElapsedMilliseconds, stoppingToken);
                }
            }

            try { await Task.Delay(TimeSpan.FromSeconds(intervaloSegundos), stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task GravarHeartbeatAsync(
        string servico, string status, string? detalhe,
        int? itensProcessados, int? duracaoMs, CancellationToken ct)
    {
        try
        {
            using var scope = serviceProvider.CreateScope();
            var recorder = scope.ServiceProvider.GetRequiredService<IHeartbeatRecorder>();
            await recorder.RecordAsync(servico, status, detalhe, itensProcessados, duracaoMs, ct);
        }
        catch (Exception ex)
        {
            // Heartbeat eh best-effort — nao quebrar o servico se falhar.
            logger.LogWarning(ex, "Falha ao gravar heartbeat do SlaMonitor");
        }
    }

    /// <returns>Quantidade de tickets avaliados; -1 quando o advisory lock estava detido por outra replica.</returns>
    private async Task<int> ExecutarTickAsync(CancellationToken ct)
    {
        using var scope = serviceProvider.CreateScope();
        var sp = scope.ServiceProvider;
        var advisoryLock = sp.GetRequiredService<PostgresAdvisoryLock>();

        int candidatosCount = 0;
        var locked = await advisoryLock.TentarExecutarAsync(LockId, async token =>
        {
            ct = token;
            var db = sp.GetRequiredService<EasyStockDbContext>();
            var notificador = sp.GetRequiredService<INotificadorService>();

            var agora = DateTime.UtcNow;

            // Carregar candidatos: tickets ativos com prazo de resposta OU resolucao definido
            // e que ainda nao tem todas as flags de violado true.
            var candidatos = await db.AdminTickets
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

            candidatosCount = candidatos.Count;
            foreach (var snap in candidatos)
            {
                await ProcessarTicketAsync(snap, agora, db, notificador, token);
            }

            await db.CommitAsync();
        }, ct);

        return locked ? candidatosCount : -1;
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
            var sql = tipo == "Resposta"
                ? "UPDATE admin_tickets SET \"SlaRespostaViolado\" = true, \"AlteradoEm\" = {0} WHERE \"Id\" = {1}"
                : "UPDATE admin_tickets SET \"SlaResolucaoViolado\" = true, \"AlteradoEm\" = {0} WHERE \"Id\" = {1}";
            await db.Database.ExecuteSqlRawAsync(sql, [agora, snap.Id], ct);

            db.TicketHistoricos.Add(EasyStock.Domain.Entities.TicketHistorico.Criar(
                snap.Id, autorId: null, EasyStock.Domain.Enums.TicketAcaoHistorico.SlaViolado,
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
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE admin_tickets SET \"UltimoAlerta80PctEm\" = {0} WHERE \"Id\" = {1}",
                [agora, snap.Id], ct);
        }
        else if (pctDecorrido >= 0.50 && snap.UltimoAlerta50PctEm is null)
        {
            await DispararAlertaProximoAsync(snap, tipo, 50, minutosRestantes, agora, db, notificador, ct);
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE admin_tickets SET \"UltimoAlerta50PctEm\" = {0} WHERE \"Id\" = {1}",
                [agora, snap.Id], ct);
        }
    }

    private async Task DispararAlertaProximoAsync(
        TicketSlaSnapshot snap, string tipo, int percentual, int minutosRestantes,
        DateTime agora, EasyStockDbContext db, INotificadorService notificador, CancellationToken ct)
    {
        SlaProximoCounter.Add(1,
            new KeyValuePair<string, object?>("prioridade", snap.Prioridade.ToString()),
            new KeyValuePair<string, object?>("percentual", percentual));

        db.TicketHistoricos.Add(EasyStock.Domain.Entities.TicketHistorico.Criar(
            snap.Id, autorId: null, EasyStock.Domain.Enums.TicketAcaoHistorico.SlaProximoVencer,
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
