using EasyStock.Api.Controllers;
using EasyStock.Api.Observability;

namespace EasyStock.Api.BackgroundServices;

/// <summary>
/// Envia um relatório diário de diagnóstico por email, consolidando erros,
/// endpoints lentos e padrões críticos detectados nas últimas 24 horas.
/// Configurável via DiagnosticoRelatorio:EmailDestino e DiagnosticoRelatorio:HoraEnvioUtc.
/// </summary>
public sealed class DiagnosticoEmailReportJob(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    HealthSnapshotService healthSnapshotService,
    ILogger<DiagnosticoEmailReportJob> logger) : BackgroundService
{
    private string GetLogDirectory() =>
        configuration["LogSettings:LogDirectory"] is { Length: > 0 } configured
            ? configured
            : Path.Combine(AppContext.BaseDirectory, "logs");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var emailDestino = configuration["DiagnosticoRelatorio:EmailDestino"];
        if (string.IsNullOrWhiteSpace(emailDestino))
        {
            logger.LogInformation("DiagnosticoEmailReportJob: EmailDestino não configurado. Serviço encerrado.");
            return;
        }

        var targetHour = configuration.GetValue<int>("DiagnosticoRelatorio:HoraEnvioUtc", 7);
        logger.LogInformation("DiagnosticoEmailReportJob iniciado — enviará relatório às {Hour}h UTC para {Email}.", targetHour, emailDestino);

        while (!stoppingToken.IsCancellationRequested)
        {
            var now = DateTime.UtcNow;
            var nextRun = now.Date.AddHours(targetHour);
            if (nextRun <= now) nextRun = nextRun.AddDays(1);

            try { await Task.Delay(nextRun - now, stoppingToken); }
            catch (OperationCanceledException) { return; }

            try
            {
                await EnviarRelatorioAsync(emailDestino, stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Erro ao enviar relatório diagnóstico diário.");
            }
        }
    }

    private async Task EnviarRelatorioAsync(string emailDestino, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var emailService = scope.ServiceProvider.GetRequiredService<IEmailService>();

        // Só envia se o SMTP real estiver configurado
        if (emailService.GetType().Name == "ConsoleEmailService")
        {
            logger.LogInformation("DiagnosticoEmailReportJob: SMTP não configurado (ConsoleEmailService). Relatório não enviado.");
            return;
        }

        var cutoff = DateTime.UtcNow.AddHours(-24);
        var logsDir = GetLogDirectory();

        // Dados dos health snapshots
        var snapshots = healthSnapshotService.GetSnapshots();
        var recentSnaps = snapshots.Where(s => s.Timestamp.UtcDateTime >= cutoff).ToList();
        double? uptime = null;
        if (recentSnaps.Count > 0)
            uptime = Math.Round(recentSnaps.Count(s => s.OverallStatus != "critical") * 100.0 / recentSnaps.Count, 2);

        // Dados dos logs
        var entries = new List<EnhancedLogEntry>();
        try { entries = DiagnosticoLogAnalyzer.ParseAllLogFiles(logsDir, cutoff); }
        catch (Exception ex) { logger.LogWarning(ex, "Erro ao ler logs para relatório."); }

        var totalErrors = entries.Count(e => e.Level is "ERROR" or "FATAL");
        var patterns = DiagnosticoLogAnalyzer.DetectPatterns(entries);
        var criticalPatterns = patterns.Where(p => p.Severidade == "critical").ToList();

        var slowEndpoints = entries
            .Where(e => e.Categoria == "http_request" && e.ElapsedMs.HasValue)
            .GroupBy(e => e.Endpoint ?? "unknown")
            .Select(g => new { endpoint = g.Key, avgMs = g.Average(e => e.ElapsedMs!.Value), count = g.Count() })
            .Where(x => x.avgMs > 500)
            .OrderByDescending(x => x.avgMs)
            .Take(5)
            .ToList();

        var html = BuildHtmlRelatorio(
            data: DateTime.UtcNow,
            totalErrors: totalErrors,
            uptime: uptime,
            snapshotsAnalisados: recentSnaps.Count,
            criticalPatterns: criticalPatterns,
            slowEndpoints: slowEndpoints.Select(x => (x.endpoint, x.avgMs, x.count)).ToList());

        var subject = $"[EasyStock] Relatório Diagnóstico — {DateTime.UtcNow:dd/MM/yyyy}";
        await emailService.SendAsync(emailDestino, subject, html, isHtml: true);
        logger.LogInformation("Relatório diagnóstico diário enviado para {Email}. Erros 24h: {Erros}.", emailDestino, totalErrors);
    }

    private static string BuildHtmlRelatorio(
        DateTime data,
        int totalErrors,
        double? uptime,
        int snapshotsAnalisados,
        List<DetectedPattern> criticalPatterns,
        List<(string endpoint, double avgMs, int count)> slowEndpoints)
    {
        var statusColor = totalErrors == 0 ? "#16a34a" : totalErrors < 10 ? "#d97706" : "#dc2626";
        var uptimeColor = uptime == null ? "#64748b" : uptime >= 99.9 ? "#16a34a" : uptime >= 99 ? "#d97706" : "#dc2626";

        var criticalHtml = criticalPatterns.Count > 0
            ? string.Join("", criticalPatterns.Select(p =>
                $"<tr><td style='padding:6px 12px;border-bottom:1px solid #e2e8f0'>{System.Net.WebUtility.HtmlEncode(p.Tipo)}</td>" +
                $"<td style='padding:6px 12px;border-bottom:1px solid #e2e8f0'>{System.Net.WebUtility.HtmlEncode(p.Descricao)}</td>" +
                $"<td style='padding:6px 12px;border-bottom:1px solid #e2e8f0'>{p.Ocorrencias}</td></tr>"))
            : "<tr><td colspan='3' style='padding:12px;color:#64748b;text-align:center'>Nenhum padrão crítico detectado ✓</td></tr>";

        var slowHtml = slowEndpoints.Count > 0
            ? string.Join("", slowEndpoints.Select(x =>
                $"<tr><td style='padding:6px 12px;border-bottom:1px solid #e2e8f0;font-family:monospace'>{System.Net.WebUtility.HtmlEncode(x.endpoint)}</td>" +
                $"<td style='padding:6px 12px;border-bottom:1px solid #e2e8f0'>{x.avgMs:F0}ms</td>" +
                $"<td style='padding:6px 12px;border-bottom:1px solid #e2e8f0'>{x.count}</td></tr>"))
            : "<tr><td colspan='3' style='padding:12px;color:#64748b;text-align:center'>Sem endpoints lentos ✓</td></tr>";

        return $"""
            <!DOCTYPE html>
            <html lang="pt-BR">
            <head><meta charset="utf-8"><title>Relatório Diagnóstico EasyStock</title></head>
            <body style="font-family:system-ui,-apple-system,sans-serif;background:#f8fafc;margin:0;padding:24px">
              <div style="max-width:640px;margin:0 auto;background:#fff;border-radius:8px;overflow:hidden;box-shadow:0 1px 3px rgba(0,0,0,.1)">
                <div style="background:#1e293b;color:#f8fafc;padding:20px 24px">
                  <h1 style="margin:0;font-size:18px">EasyStock — Relatório Diagnóstico Diário</h1>
                  <p style="margin:4px 0 0;color:#94a3b8;font-size:13px">{data:dd/MM/yyyy} · gerado às {data:HH:mm} UTC</p>
                </div>
                <div style="padding:24px">
                  <!-- KPIs -->
                  <div style="display:grid;grid-template-columns:1fr 1fr;gap:16px;margin-bottom:24px">
                    <div style="background:#f8fafc;border-radius:6px;padding:16px;border:1px solid #e2e8f0">
                      <div style="font-size:12px;color:#64748b;margin-bottom:4px">Erros (24h)</div>
                      <div style="font-size:28px;font-weight:700;color:{statusColor}">{totalErrors}</div>
                    </div>
                    <div style="background:#f8fafc;border-radius:6px;padding:16px;border:1px solid #e2e8f0">
                      <div style="font-size:12px;color:#64748b;margin-bottom:4px">Uptime ({snapshotsAnalisados} snapshots)</div>
                      <div style="font-size:28px;font-weight:700;color:{uptimeColor}">{(uptime.HasValue ? uptime.Value.ToString("F1") + "%" : "N/D")}</div>
                    </div>
                  </div>
                  <!-- Padrões críticos -->
                  <h2 style="font-size:14px;font-weight:600;color:#1e293b;margin:0 0 8px">Padrões Críticos Detectados</h2>
                  <table style="width:100%;border-collapse:collapse;font-size:13px;margin-bottom:24px">
                    <thead><tr style="background:#f1f5f9">
                      <th style="padding:8px 12px;text-align:left;color:#64748b;font-weight:500">Tipo</th>
                      <th style="padding:8px 12px;text-align:left;color:#64748b;font-weight:500">Descrição</th>
                      <th style="padding:8px 12px;text-align:right;color:#64748b;font-weight:500">Ocorrências</th>
                    </tr></thead>
                    <tbody>{criticalHtml}</tbody>
                  </table>
                  <!-- Endpoints lentos -->
                  <h2 style="font-size:14px;font-weight:600;color:#1e293b;margin:0 0 8px">Endpoints Mais Lentos (&gt;500ms)</h2>
                  <table style="width:100%;border-collapse:collapse;font-size:13px;margin-bottom:24px">
                    <thead><tr style="background:#f1f5f9">
                      <th style="padding:8px 12px;text-align:left;color:#64748b;font-weight:500">Endpoint</th>
                      <th style="padding:8px 12px;text-align:right;color:#64748b;font-weight:500">Média</th>
                      <th style="padding:8px 12px;text-align:right;color:#64748b;font-weight:500">Requests</th>
                    </tr></thead>
                    <tbody>{slowHtml}</tbody>
                  </table>
                  <p style="font-size:12px;color:#94a3b8;margin:0">Acesse a <a href="/diagnostico" style="color:#6366f1">Central de Operações</a> para detalhes completos.</p>
                </div>
              </div>
            </body>
            </html>
            """;
    }
}
