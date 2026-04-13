using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using EasyStock.Api.BackgroundServices;
using EasyStock.Api.Configuration;
using EasyStock.Api.Observability;
using EasyStock.Infra.Postgre.Data;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Storage;
using Azure.Storage.Files.Shares;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;

namespace EasyStock.Api.Controllers;

[ApiController]
[Route("api/diagnostico")]
[Route("diagnostico")]
[AllowAnonymous]
[ApiExplorerSettings(GroupName = "v1-ptbr")]
public sealed class DiagnosticoController(
    ResolvedInfrastructureState infraState,
    IConfiguration configuration,
    IDistributedCache cache,
    IEmailService emailService,
    HealthSnapshotService healthSnapshotService,
    IHttpClientFactory httpClientFactory,
    ILogger<DiagnosticoController> logger) : ControllerBase
{
    [HttpGet("ping")]
    public IActionResult Ping() => Ok(new { pong = true, timestamp = DateTimeOffset.UtcNow });

    [HttpGet]
    public async Task<IActionResult> Diagnostico(CancellationToken ct)
    {
        var result = new DiagnosticoResult
        {
            Status = "ok",
            Timestamp = DateTimeOffset.UtcNow,
            Ambiente = infraState.Environment,
            Uptime = FormatUptime(DateTimeOffset.UtcNow - infraState.StartupTime),
            Versao = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0",
            Banco = await GetBancoStatusAsync(ct),
            Redis = await GetRedisStatusAsync(ct),
            Smtp = GetSmtpStatus(),
            Storage = await GetStorageStatusAsync(ct),
            Ia = GetIaStatus(),
            Configuracoes = GetConfiguracoesStatus()
        };

        // Determinar status geral
        if (result.Banco.Conexao == "falha" || result.Configuracoes.JwtSecretPresente == false)
            result.Status = "critical";
        else if (result.Redis.Conexao == "falha" || result.Banco.Fallback ||
                 result.Configuracoes.JwtSecretSeguro == false)
            result.Status = "degraded";

        // Causas prováveis
        result.CausasProvaveis = BuildCausasProvaveis(result);

        if (HttpContext.Request.Headers.Accept.Any(a => a?.Contains("text/html") == true))
        {
            // Gather extra data for the full dashboard
            var snapshots = healthSnapshotService.GetSnapshots();
            EnhancedLogsResult? enhancedLogs = null;
            try
            {
                var cutoff = DateTime.UtcNow.AddHours(-48);
                var logsDir = GetLogDirectory();
                if (Directory.Exists(logsDir))
                {
                    var dir = new DirectoryInfo(logsDir);
                    var logFiles = dir.GetFiles("easystock-*.log")
                        .OrderByDescending(f => f.LastWriteTimeUtc)
                        .Where(f => f.LastWriteTimeUtc >= cutoff)
                        .OrderBy(f => f.Name)
                        .ToList();

                    if (logFiles.Count > 0)
                    {
                        var allEntries = new List<EnhancedLogEntry>();
                        foreach (var file in logFiles)
                        {
                            allEntries.AddRange(DiagnosticoLogAnalyzer.ParseEnhancedLogFile(file.FullName, cutoff));
                            if (allEntries.Count > 5000) { allEntries = allEntries.TakeLast(5000).ToList(); break; }
                        }

                        enhancedLogs = new EnhancedLogsResult
                        {
                            Disponivel = true,
                            QueryTimestamp = DateTimeOffset.UtcNow,
                            PeriodoHoras = 48,
                            TotalEntries = allEntries.Count,
                            Entradas = allEntries.TakeLast(500).ToArray(),
                            Resumo = DiagnosticoLogAnalyzer.BuildLogSummary(allEntries),
                            Padroes = DiagnosticoLogAnalyzer.DetectPatterns(allEntries, infraState.IsFallback).ToArray()
                        };
                    }
                }
            }
            catch (Exception ex) { logger.LogDebug(ex, "Log parsing failed — dashboard will render without log data."); }

            return Content(RenderHtml(result, snapshots, enhancedLogs), "text/html; charset=utf-8");
        }

        return Ok(result);
    }

    [HttpGet("banco")]
    public async Task<IActionResult> TesteBanco(CancellationToken ct)
    {
        var status = await GetBancoStatusAsync(ct);
        return Ok(status);
    }

    [HttpGet("logs")]
    public IActionResult Logs([FromQuery] int n = 100)
    {
        n = Math.Clamp(n, 1, 200);

        var logsDir = GetLogDirectory();
        var today = DateTime.UtcNow.ToString("yyyyMMdd");
        var logFile = Path.Combine(logsDir, $"easystock-{today}.log");

        if (!System.IO.File.Exists(logFile))
        {
            var dir = new DirectoryInfo(logsDir);
            if (dir.Exists)
            {
                var latest = dir.GetFiles("easystock-*.log")
                    .OrderByDescending(f => f.LastWriteTimeUtc)
                    .FirstOrDefault();
                if (latest != null)
                    logFile = latest.FullName;
            }
        }

        if (!System.IO.File.Exists(logFile))
        {
            return Ok(new LogsInfo
            {
                Disponivel = false,
                Motivo = "Arquivo de log não encontrado. O log em arquivo pode não estar configurado neste ambiente."
            });
        }

        try
        {
            string[] lines;
            using (var fs = new FileStream(logFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = new StreamReader(fs))
            {
                var allLines = new List<string>();
                while (!reader.EndOfStream)
                {
                    var line = reader.ReadLine();
                    if (line is not null) allLines.Add(line);
                }
                lines = allLines.TakeLast(n).ToArray();
            }

            var entries = lines
                .Select(ParseLogLine)
                .Where(e => e is not null)
                .Cast<LogEntry>()
                .ToArray();

            return Ok(new LogsInfo
            {
                Disponivel = true,
                Arquivo = Path.GetFileName(logFile),
                TotalLinhas = lines.Length,
                Entradas = entries
            });
        }
        catch (Exception ex)
        {
            return Ok(new LogsInfo
            {
                Disponivel = false,
                Motivo = $"Erro ao ler arquivo de log: {ex.Message}"
            });
        }
    }

    // ──────────────────────────────────────────────────────────────────────
    // NOVOS ENDPOINTS — Central de Operações Inteligente
    // ──────────────────────────────────────────────────────────────────────

    [HttpGet("logs/enhanced")]
    public IActionResult EnhancedLogs([FromQuery] int hours = 24)
    {
        hours = Math.Clamp(hours, 1, 72);
        var cutoff = DateTime.UtcNow.AddHours(-hours);
        var logsDir = GetLogDirectory();

        if (!Directory.Exists(logsDir))
            return Ok(new EnhancedLogsResult { Disponivel = false, Motivo = "Diretório de logs não encontrado." });

        try
        {
            var allEntries = DiagnosticoLogAnalyzer.ParseAllLogFiles(logsDir, cutoff);
            if (allEntries.Count == 0)
                return Ok(new EnhancedLogsResult { Disponivel = false, Motivo = "Nenhum arquivo de log encontrado para o período solicitado." });

            return Ok(new EnhancedLogsResult
            {
                Disponivel = true,
                QueryTimestamp = DateTimeOffset.UtcNow,
                PeriodoHoras = hours,
                TotalEntries = allEntries.Count,
                Entradas = allEntries.ToArray(),
                Resumo = DiagnosticoLogAnalyzer.BuildLogSummary(allEntries),
                Padroes = DiagnosticoLogAnalyzer.DetectPatterns(allEntries, infraState.IsFallback).ToArray()
            });
        }
        catch (Exception ex)
        {
            return Ok(new EnhancedLogsResult { Disponivel = false, Motivo = $"Erro ao processar logs: {ex.Message}" });
        }
    }

    [HttpGet("logs/live")]
    public IActionResult LiveLogs([FromQuery] string? since = null)
    {
        var cutoff = DateTimeOffset.TryParse(since, null, System.Globalization.DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed.UtcDateTime
            : DateTime.UtcNow.AddMinutes(-5);

        var logsDir = GetLogDirectory();
        if (!Directory.Exists(logsDir))
            return Ok(new { rows = Array.Empty<string>(), count = 0 });

        try
        {
            var dir = new DirectoryInfo(logsDir);
            var logFiles = dir.GetFiles("easystock-*.log")
                .Where(f => f.LastWriteTimeUtc >= cutoff.AddHours(-1))
                .OrderBy(f => f.Name)
                .ToList();

            var allEntries = new List<EnhancedLogEntry>();
            foreach (var file in logFiles)
            {
                allEntries.AddRange(DiagnosticoLogAnalyzer.ParseEnhancedLogFile(file.FullName, cutoff));
                if (allEntries.Count > 200) break;
            }

            var rows = allEntries.TakeLast(200).Select(e =>
            {
                var levelClass = e.Level switch
                {
                    "ERROR" or "FATAL" => "log-error",
                    "WARN" => "log-warn",
                    "DEBUG" => "log-debug",
                    _ => "log-info"
                };
                var cat = e.Categoria switch
                {
                    "http_request" => $"<span class='log-cat cat-http'>{e.HttpMethod} {e.StatusCode}</span>",
                    "migration" => "<span class='log-cat cat-migration'>MIGRATION</span>",
                    "startup" => "<span class='log-cat cat-startup'>STARTUP</span>",
                    "error" => "<span class='log-cat cat-error'>ERROR</span>",
                    "db_operation" => "<span class='log-cat cat-db'>DB</span>",
                    _ => ""
                };
                var elapsed = e.ElapsedMs.HasValue ? $"<span class='log-elapsed'>{e.ElapsedMs:F0}ms</span>" : "";
                var msg = System.Net.WebUtility.HtmlEncode(e.Message.Length > 500 ? e.Message[..500] + "..." : e.Message);
                var exc = e.Exception != null ? $"<div class='log-exception'>{System.Net.WebUtility.HtmlEncode(e.Exception)}</div>" : "";
                return $"<div class='log-row {levelClass}' data-level='{e.Level}' data-cat='{e.Categoria}'>" +
                       $"<span class='log-time'>{e.Timestamp:HH:mm:ss}</span>" +
                       $"<span class='log-level'>{e.Level}</span>" +
                       $"{cat}{elapsed}" +
                       $"<span class='log-msg'>{msg}</span>{exc}</div>";
            }).ToArray();

            return Ok(new { rows, count = rows.Length });
        }
        catch
        {
            return Ok(new { rows = Array.Empty<string>(), count = 0 });
        }
    }

    [HttpGet("endpoints")]
    public async Task<IActionResult> TestEndpoints(CancellationToken ct)
    {
        // Determine self base URL
        var request = HttpContext.Request;
        var baseUrl = $"{request.Scheme}://{request.Host}";

        var routes = new[]
        {
            ("/diagnostico/ping", "GET", 200),
            ("/health", "GET", 200),
            ("/health/live", "GET", 200),
            ("/health/ready", "GET", 200),
            ("/api/diagnostico", "GET", 200),
            ("/api/produtos", "GET", 401),
            ("/api/categorias", "GET", 401),
            ("/api/estoque", "GET", 401),
            ("/api/movimentacoes", "GET", 401),
            ("/api/analytics/dashboard", "GET", 401),
            ("/api/notificacoes", "GET", 401),
            ("/api/auth/me", "GET", 401),
            ("/swagger/v1-ptbr/swagger.json", "GET", 200),
        };

        var client = httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(5);

        var results = new List<EndpointTestResult>();

        foreach (var (route, method, expectedStatus) in routes)
        {
            var result = new EndpointTestResult
            {
                Rota = route,
                Metodo = method,
                TestadoEm = DateTimeOffset.UtcNow
            };

            try
            {
                var sw = Stopwatch.StartNew();
                var response = await client.GetAsync(baseUrl + route, ct);
                sw.Stop();

                result.StatusCode = (int)response.StatusCode;
                result.LatenciaMs = sw.ElapsedMilliseconds;

                // Auth-protected endpoints returning 401 are "alive"
                var isExpectedStatus = result.StatusCode == expectedStatus ||
                                       (expectedStatus == 401 && result.StatusCode == 401);
                var isHealthy = result.StatusCode < 500 && isExpectedStatus;

                result.Status = isHealthy
                    ? (result.LatenciaMs < 300 ? "ok" : result.LatenciaMs < 1000 ? "slow" : "very_slow")
                    : "error";
            }
            catch (TaskCanceledException)
            {
                result.Status = "timeout";
                result.LatenciaMs = 5000;
                result.StatusCode = 0;
            }
            catch (Exception ex)
            {
                result.Status = "error";
                result.Erro = ex.Message;
                result.StatusCode = 0;
            }

            results.Add(result);
        }

        var healthy = results.Count(r => r.Status == "ok");
        var slow = results.Count(r => r.Status is "slow" or "very_slow");
        var failed = results.Count(r => r.Status is "error" or "timeout");

        return Ok(new EndpointsTestResponse
        {
            Resultados = results.ToArray(),
            Saudaveis = healthy,
            Lentos = slow,
            Falhas = failed,
            TestadoEm = DateTimeOffset.UtcNow
        });
    }

    [HttpGet("historico")]
    public IActionResult HealthHistory()
    {
        var snapshots = healthSnapshotService.GetSnapshots();
        return Ok(new HealthHistoryResponse
        {
            Snapshots = snapshots.ToArray(),
            Desde = snapshots.Count > 0 ? snapshots[0].Timestamp : DateTimeOffset.UtcNow,
            Total = snapshots.Count
        });
    }

    [HttpPost("logs/limpar")]
    public IActionResult LimparLogs()
    {
        var logsDir = GetLogDirectory();
        if (!Directory.Exists(logsDir))
            return Ok(new { success = false, mensagem = "Diretório de logs não encontrado.", arquivosMovidos = 0 });

        var lixeiraDir = Path.Combine(logsDir, "lixeira");
        try { Directory.CreateDirectory(lixeiraDir); }
        catch (Exception ex) { return Ok(new { success = false, mensagem = $"Não foi possível criar lixeira: {ex.Message}", arquivosMovidos = 0 }); }

        var arquivos = Directory.GetFiles(logsDir, "easystock-*.log");
        var movidos = 0;
        var avisos = new List<string>();

        foreach (var arquivo in arquivos)
        {
            var dest = Path.Combine(lixeiraDir, $"{DateTime.UtcNow:yyyyMMdd-HHmmss}_{Path.GetFileName(arquivo)}");
            try
            {
                System.IO.File.Move(arquivo, dest);
                movidos++;
            }
            catch (IOException)
            {
                // Arquivo bloqueado pelo Serilog (dia corrente) — copiar para lixeira e truncar in-place
                try
                {
                    System.IO.File.Copy(arquivo, dest, overwrite: true);
                    using var fs = new FileStream(arquivo, FileMode.Truncate, FileAccess.Write, FileShare.ReadWrite);
                    movidos++;
                    avisos.Add(Path.GetFileName(arquivo) + ": copiado para lixeira e truncado in-place (em uso pelo Serilog)");
                }
                catch (Exception ex2)
                {
                    avisos.Add(Path.GetFileName(arquivo) + ": não foi possível mover/copiar — " + ex2.Message);
                }
            }
            catch (Exception ex)
            {
                avisos.Add(Path.GetFileName(arquivo) + ": " + ex.Message);
            }
        }

        return Ok(new
        {
            success = true,
            arquivosMovidos = movidos,
            destino = "logs/lixeira/",
            mensagem = movidos > 0
                ? $"{movidos} arquivo(s) de log movido(s) para a lixeira."
                : "Nenhum arquivo de log encontrado.",
            avisos
        });
    }

    [HttpGet("logs/lixeira")]
    public IActionResult InspecionarLixeira()
    {
        var lixeiraDir = Path.Combine(GetLogDirectory(), "lixeira");
        if (!Directory.Exists(lixeiraDir))
            return Ok(new { arquivos = Array.Empty<object>(), total = 0, tamanhoTotalBytes = 0L });

        var arquivos = new DirectoryInfo(lixeiraDir)
            .GetFiles("*")
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .Select(f => new { nome = f.Name, tamanhoBytes = f.Length, movidoEm = f.LastWriteTimeUtc })
            .ToArray();

        return Ok(new
        {
            arquivos,
            total = arquivos.Length,
            tamanhoTotalBytes = arquivos.Sum(a => a.tamanhoBytes)
        });
    }

    [HttpPost("logs/lixeira/esvaziar")]
    public IActionResult EsvaziarLixeira()
    {
        var lixeiraDir = Path.Combine(GetLogDirectory(), "lixeira");
        if (!Directory.Exists(lixeiraDir))
            return Ok(new { success = true, arquivosExcluidos = 0, mensagem = "Lixeira já vazia." });

        var arquivos = Directory.GetFiles(lixeiraDir);
        var excluidos = 0;
        var erros = new List<string>();

        foreach (var arquivo in arquivos)
        {
            try { System.IO.File.Delete(arquivo); excluidos++; }
            catch (Exception ex) { erros.Add(Path.GetFileName(arquivo) + ": " + ex.Message); }
        }

        return Ok(new
        {
            success = true,
            arquivosExcluidos = excluidos,
            mensagem = $"Lixeira esvaziada: {excluidos} arquivo(s) excluído(s).",
            erros
        });
    }

    [HttpGet("logs/exportar")]
    public IActionResult ExportarLogs([FromQuery] int hours = 48)
    {
        hours = Math.Clamp(hours, 1, 168);
        var logsDir = GetLogDirectory();
        if (!Directory.Exists(logsDir))
            return NotFound(new { error = "Diretório de logs não encontrado." });

        var cutoff = DateTime.UtcNow.AddHours(-hours);
        var arquivos = new DirectoryInfo(logsDir)
            .GetFiles("easystock-*.log")
            .Where(f => f.LastWriteTimeUtc >= cutoff || f.Name.Contains(DateTime.UtcNow.ToString("yyyyMMdd")))
            .OrderBy(f => f.Name)
            .ToArray();

        if (arquivos.Length == 0)
            return NotFound(new { error = "Nenhum arquivo de log encontrado para o período." });

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"# EasyStock Logs — exportado em {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine($"# Período: últimas {hours}h — {arquivos.Length} arquivo(s)");
        sb.AppendLine();

        foreach (var f in arquivos)
        {
            sb.AppendLine($"### {f.Name} ###");
            try
            {
                using var fs = new FileStream(f.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(fs);
                sb.AppendLine(reader.ReadToEnd());
            }
            catch (Exception ex)
            {
                sb.AppendLine($"[ERRO AO LER: {ex.Message}]");
            }
            sb.AppendLine();
        }

        var bytes = System.Text.Encoding.UTF8.GetBytes(sb.ToString());
        var fileName = $"easystock-logs-{DateTime.UtcNow:yyyyMMdd-HHmm}.log";
        return File(bytes, "text/plain; charset=utf-8", fileName);
    }

    [HttpPost("logs/salvar-storage")]
    public async Task<IActionResult> SalvarLogsStorage([FromServices] IFileStorage fileStorage, CancellationToken ct)
    {
        var logsDir = GetLogDirectory();
        if (!Directory.Exists(logsDir))
            return NotFound(new { error = "Diretório de logs não encontrado." });

        var arquivos = new DirectoryInfo(logsDir)
            .GetFiles("easystock-*.log")
            .OrderBy(f => f.Name)
            .ToArray();

        if (arquivos.Length == 0)
            return Ok(new { success = false, mensagem = "Nenhum arquivo de log encontrado." });

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"# EasyStock Logs — salvo em {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine($"# {arquivos.Length} arquivo(s)");
        sb.AppendLine();

        foreach (var f in arquivos)
        {
            sb.AppendLine($"### {f.Name} ###");
            try
            {
                using var fs = new FileStream(f.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(fs);
                sb.AppendLine(reader.ReadToEnd());
            }
            catch (Exception ex)
            {
                sb.AppendLine($"[ERRO AO LER: {ex.Message}]");
            }
            sb.AppendLine();
        }

        var bytes = System.Text.Encoding.UTF8.GetBytes(sb.ToString());
        var fileName = $"logs/easystock-logs-{DateTime.UtcNow:yyyyMMdd-HHmmss}.log";

        try
        {
            var result = await fileStorage.UploadAsync(new FileUploadRequest(
                BucketPath: "logs",
                FileName: $"easystock-logs-{DateTime.UtcNow:yyyyMMdd-HHmmss}.log",
                ContentType: "text/plain",
                Content: bytes,
                IsPublic: false), ct);

            return Ok(new
            {
                success = true,
                mensagem = "Logs salvos no storage com sucesso.",
                storageKey = result.StorageKey,
                url = result.Url,
                tamanhoBytes = result.Size
            });
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Falha ao salvar logs no storage.");
            return StatusCode(500, new { success = false, mensagem = $"Erro ao salvar no storage: {ex.Message}" });
        }
    }

    // ──────────────────────────────────────────────────────────────────────
    // Novos endpoints — melhorias da central de diagnóstico
    // ──────────────────────────────────────────────────────────────────────

    [HttpGet("eventos")]
    public IActionResult Eventos([FromQuery] int hours = 48)
    {
        hours = Math.Clamp(hours, 1, 72);
        var cutoff = DateTime.UtcNow.AddHours(-hours);
        var logsDir = GetLogDirectory();

        try
        {
            var entries = DiagnosticoLogAnalyzer.ParseAllLogFiles(logsDir, cutoff);
            var eventos = DiagnosticoLogAnalyzer.ExtractTimelineEvents(entries);
            return Ok(new { disponivel = true, eventos, periodoHoras = hours });
        }
        catch (Exception ex)
        {
            return Ok(new { disponivel = false, eventos = Array.Empty<TimelineEvent>(), motivo = ex.Message });
        }
    }

    [HttpGet("slo")]
    public IActionResult Slo([FromQuery] int hours = 24)
    {
        hours = Math.Clamp(hours, 1, 168);
        var cutoff = DateTime.UtcNow.AddHours(-hours);

        // Uptime a partir dos health snapshots
        var snapshots = healthSnapshotService.GetSnapshots();
        var periodSnaps = snapshots.Where(s => s.Timestamp >= cutoff).ToList();
        double? uptime = null;
        if (periodSnaps.Count > 0)
            uptime = Math.Round(periodSnaps.Count(s => s.OverallStatus != "critical") * 100.0 / periodSnaps.Count, 2);

        // Latências e taxas a partir dos logs
        var logsDir = GetLogDirectory();
        double? avg = null, p95 = null;
        int totalRequests = 0, totalErrors = 0;

        try
        {
            var entries = DiagnosticoLogAnalyzer.ParseAllLogFiles(logsDir, cutoff.ToLocalTime() < DateTime.MinValue.AddHours(1) ? cutoff.ToUniversalTime() : cutoff);
            var httpEntries = entries.Where(e => e.Categoria == "http_request" && e.ElapsedMs.HasValue).ToList();

            if (httpEntries.Count > 0)
            {
                var sorted = httpEntries.Select(e => e.ElapsedMs!.Value).OrderBy(v => v).ToList();
                avg = Math.Round(sorted.Average(), 1);
                p95 = Math.Round(sorted[Math.Min(sorted.Count - 1, (int)(sorted.Count * 0.95))], 1);
            }

            totalRequests = httpEntries.Count;
            totalErrors = entries.Count(e => e.Level is "ERROR" or "FATAL");
        }
        catch { /* logs indisponíveis — retornar parcial */ }

        var errorRate = totalRequests > 0 ? Math.Round(totalErrors / (double)totalRequests, 4) : 0.0;

        return Ok(new
        {
            calculadoEm = DateTimeOffset.UtcNow,
            periodoHoras = hours,
            uptime24h = uptime,
            avgResponseTimeMs = avg,
            p95ResponseTimeMs = p95,
            errorRate,
            totalRequests,
            totalErrors,
            snapshotsAnalisados = periodSnaps.Count,
            fontes = new { snapshots = periodSnaps.Count > 0, logs = avg.HasValue }
        });
    }

    [HttpPost("alertas/{alertaId}/ack")]
    public async Task<IActionResult> AckAlerta(string alertaId, [FromBody] AckAlertaRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(alertaId) || alertaId.Length > 32)
            return BadRequest(new { error = "AlertaId inválido." });

        if (request.Status is not ("visto" or "em_investigacao" or "resolvido"))
            return BadRequest(new { error = "Status inválido. Use: visto, em_investigacao ou resolvido." });

        var ack = new AlertaAck
        {
            AlertaId = alertaId,
            Status = request.Status,
            Observacao = request.Observacao?.Trim(),
            AtualizadoEm = DateTimeOffset.UtcNow
        };

        var cacheKey = $"diag:ack:{alertaId}";
        await cache.SetStringAsync(cacheKey, System.Text.Json.JsonSerializer.Serialize(ack),
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(7) }, ct);

        return Ok(ack);
    }

    [HttpGet("alertas/acks")]
    public async Task<IActionResult> GetAcks([FromQuery] string? ids = null, CancellationToken ct = default)
    {
        // ids = comma-separated list of alertaIds to look up
        var idList = (ids ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                                 .Take(50).ToArray();

        var acks = new List<AlertaAck>();
        foreach (var id in idList)
        {
            try
            {
                var json = await cache.GetStringAsync($"diag:ack:{id}", ct);
                if (json is not null)
                {
                    var ack = System.Text.Json.JsonSerializer.Deserialize<AlertaAck>(json);
                    if (ack is not null) acks.Add(ack);
                }
            }
            catch { /* cache indisponível */ }
        }

        return Ok(new { acks });
    }

    [HttpDelete("alertas/{alertaId}/ack")]
    public async Task<IActionResult> RemoverAck(string alertaId, CancellationToken ct)
    {
        await cache.RemoveAsync($"diag:ack:{alertaId}", ct);
        return Ok(new { success = true });
    }

    [HttpGet("queries-lentas")]
    public async Task<IActionResult> QueriesLentas(CancellationToken ct)
    {
        if (!infraState.DatabaseProvider.Equals("postgresql", StringComparison.OrdinalIgnoreCase))
            return Ok(new { disponivel = false, motivo = "Apenas disponível com PostgreSQL." });

        try
        {
            using var scope = HttpContext.RequestServices.CreateScope();
            var db = scope.ServiceProvider.GetService<EasyStockDbContext>();
            if (db is null)
                return Ok(new { disponivel = false, motivo = "DbContext não disponível." });

            var conn = db.Database.GetDbConnection();
            await conn.OpenAsync(ct);

            const string sql = """
                SELECT query,
                       calls,
                       total_exec_time / calls AS avg_ms,
                       stddev_exec_time         AS stddev_ms,
                       CASE WHEN calls > 0 THEN rows::float / calls ELSE 0 END AS avg_rows
                FROM pg_stat_statements
                WHERE calls > 10
                ORDER BY avg_ms DESC
                LIMIT 20
                """;

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            await using var reader = await cmd.ExecuteReaderAsync(ct);

            var queries = new List<object>();
            while (await reader.ReadAsync(ct))
            {
                queries.Add(new
                {
                    query     = reader.GetString(0),
                    calls     = reader.GetInt64(1),
                    avgMs     = Math.Round(reader.GetDouble(2), 2),
                    stddevMs  = reader.IsDBNull(3) ? (double?)null : Math.Round(reader.GetDouble(3), 2),
                    avgRows   = Math.Round(reader.GetDouble(4), 1)
                });
            }

            return Ok(new { disponivel = true, queries });
        }
        catch (Exception ex) when (ex.Message.Contains("pg_stat_statements", StringComparison.OrdinalIgnoreCase)
                                || ex.Message.Contains("does not exist", StringComparison.OrdinalIgnoreCase))
        {
            return Ok(new
            {
                disponivel = false,
                motivo = "Extensão pg_stat_statements não está ativa.",
                instrucoesPgStatStatements =
                    "Execute no PostgreSQL: CREATE EXTENSION IF NOT EXISTS pg_stat_statements; " +
                    "e adicione 'shared_preload_libraries = ''pg_stat_statements''' ao postgresql.conf, então reinicie o serviço."
            });
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Erro ao consultar queries lentas.");
            return Ok(new { disponivel = false, motivo = ex.Message });
        }
    }

    [HttpGet("health/empresas")]
    public async Task<IActionResult> HealthEmpresas(CancellationToken ct)
    {
        if (!infraState.DatabaseProvider.Equals("postgresql", StringComparison.OrdinalIgnoreCase) &&
            !infraState.DatabaseProvider.Equals("sqlite", StringComparison.OrdinalIgnoreCase))
            return Ok(new { disponivel = false, motivo = "Apenas disponível com PostgreSQL ou SQLite." });

        try
        {
            using var scope = HttpContext.RequestServices.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();

            var empresas = await db.Empresas
                .AsNoTracking()
                .Select(e => new { e.Id, e.Nome })
                .Take(20)
                .ToListAsync(ct);

            if (empresas.Count == 0)
                return Ok(new { disponivel = true, empresas = Array.Empty<object>(), totalAnalisadas = 0, ok = 0, degraded = 0 });

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(12));

            var checks = await Task.WhenAll(empresas.Select(async empresa =>
            {
                var sw = Stopwatch.StartNew();
                try
                {
                    using var innerScope = HttpContext.RequestServices.CreateScope();
                    var innerDb = innerScope.ServiceProvider.GetRequiredService<EasyStockDbContext>();

                    var prodCount = await innerDb.Produtos
                        .AsNoTracking()
                        .CountAsync(p => p.EmpresaId == empresa.Id, cts.Token);

                    var vendaCount = await innerDb.Vendas
                        .AsNoTracking()
                        .CountAsync(v => v.EmpresaId == empresa.Id && v.DataVenda > DateTime.UtcNow.AddDays(-1), cts.Token);

                    sw.Stop();
                    var status = sw.ElapsedMilliseconds > 2000 ? "degraded" : "ok";

                    return new
                    {
                        empresaId = empresa.Id,
                        nome = empresa.Nome,
                        status,
                        latenciaMs = sw.ElapsedMilliseconds,
                        produtos = prodCount,
                        vendasUltimas24h = vendaCount,
                        erro = (string?)null
                    };
                }
                catch (OperationCanceledException)
                {
                    return new { empresaId = empresa.Id, nome = empresa.Nome, status = "timeout", latenciaMs = sw.ElapsedMilliseconds, produtos = 0, vendasUltimas24h = 0, erro = (string?)"Timeout" };
                }
                catch (Exception ex)
                {
                    return new { empresaId = empresa.Id, nome = empresa.Nome, status = "error", latenciaMs = sw.ElapsedMilliseconds, produtos = 0, vendasUltimas24h = 0, erro = (string?)ex.Message[..Math.Min(ex.Message.Length, 100)] };
                }
            }));

            return Ok(new
            {
                disponivel = true,
                empresas = checks,
                totalAnalisadas = checks.Length,
                ok = checks.Count(c => c.status == "ok"),
                degraded = checks.Count(c => c.status is "degraded" or "timeout" or "error")
            });
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Erro ao verificar saúde por empresa.");
            return Ok(new { disponivel = false, motivo = ex.Message });
        }
    }

    // ──────────────────────────────────────────────────────────────────────
    // Helpers privados (existentes)
    // ──────────────────────────────────────────────────────────────────────

    private async Task<BancoStatus> GetBancoStatusAsync(CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var status = new BancoStatus
        {
            Provider = infraState.DatabaseProvider,
            ProviderConfigurado = infraState.ConfiguredProvider,
            Fallback = infraState.IsFallback,
            Conexao = infraState.MigrationsApplied == false ? "falha" : "ok",
            MigrationsAplicadas = infraState.MigrationsApplied,
            Erro = infraState.MigrationError
        };

        try
        {
            if (infraState.DatabaseProvider is "postgresql" or "sqlite")
            {
                using var scope = HttpContext.RequestServices.CreateScope();
                var db = scope.ServiceProvider.GetService<EasyStockDbContext>();

                if (db is not null)
                {
                    var canConnect = await db.Database.CanConnectAsync(ct);
                    status.Conexao = canConnect ? "ok" : "falha";
                    if (!canConnect)
                        status.CausaProvavel = "Banco inacessível — verifique a connection string e conectividade de rede.";
                }
            }
        }
        catch (Exception ex)
        {
            status.Conexao = "falha";
            status.Erro = ex.Message;
            status.CausaProvavel = "Exceção ao conectar no banco — verifique a connection string e se o serviço está no ar.";
        }

        sw.Stop();
        status.LatenciaMs = sw.ElapsedMilliseconds;

        if (status.Fallback)
            status.CausaProvavel ??= "Banco principal indisponível — operando em modo fallback (SQLite).";

        return status;
    }

    private async Task<RedisStatus> GetRedisStatusAsync(CancellationToken ct)
    {
        var redisCs = configuration.GetConnectionString("Redis");
        if (string.IsNullOrWhiteSpace(redisCs))
            return new RedisStatus { Configurado = false, Conexao = "nao_configurado" };

        var sw = Stopwatch.StartNew();
        try
        {
            var key = "diagnostico:ping:" + Guid.NewGuid().ToString("N")[..8];
            await cache.SetStringAsync(key, "ok", new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(10)
            }, ct);
            var val = await cache.GetStringAsync(key, ct);
            sw.Stop();
            return new RedisStatus
            {
                Configurado = true,
                Conexao = val is not null ? "ok" : "falha",
                LatenciaMs = sw.ElapsedMilliseconds,
                CausaProvavel = val is null ? "Redis respondeu mas operação set/get falhou." : null
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new RedisStatus
            {
                Configurado = true,
                Conexao = "falha",
                Erro = ex.Message,
                LatenciaMs = sw.ElapsedMilliseconds,
                CausaProvavel = "Redis inacessível — verifique se o serviço está no ar e a connection string."
            };
        }
    }

    private SmtpStatus GetSmtpStatus()
    {
        var typeName = emailService.GetType().Name;
        var host = configuration["Smtp:Host"];
        return new SmtpStatus
        {
            Configurado = typeName != nameof(Infra.Async.DependencyInjection.ConsoleEmailService),
            Tipo = typeName,
            Host = host
        };
    }

    private async Task<StorageStatus> GetStorageStatusAsync(CancellationToken ct)
    {
        var provider = configuration["FileStorage:Provider"] ?? "Local";
        var status = new StorageStatus { Provider = provider };

        if (string.Equals(provider, "Local", StringComparison.OrdinalIgnoreCase))
        {
            var rootPath = configuration["FileStorage:LocalRootPath"] ?? "uploaded-files";
            status.DiretorioExiste = Directory.Exists(rootPath);
            status.Configurado = true;
        }
        else if (string.Equals(provider, "AzureFileShare", StringComparison.OrdinalIgnoreCase))
        {
            var connStr = configuration["FileStorage:AzureFileShare:ConnectionString"];
            var shareName = configuration["FileStorage:AzureFileShare:ShareName"];
            status.Configurado = !string.IsNullOrWhiteSpace(connStr)
                              && !connStr.Contains("<")
                              && !string.IsNullOrWhiteSpace(shareName);
            if (status.Configurado)
            {
                try
                {
                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    cts.CancelAfter(TimeSpan.FromSeconds(5));
                    var serviceClient = new ShareServiceClient(connStr);
                    var shareClient = serviceClient.GetShareClient(shareName);
                    await shareClient.GetPropertiesAsync(cts.Token);
                    status.DiretorioExiste = true;
                }
                catch (Azure.RequestFailedException ex)
                {
                    status.DiretorioExiste = false;
                    status.Configurado = false;
                    status.Erro = $"Azure error {ex.ErrorCode}: {ex.Message}";
                }
                catch (Exception ex)
                {
                    status.DiretorioExiste = false;
                    status.Configurado = false;
                    status.Erro = ex.Message;
                }
            }
        }
        else if (string.Equals(provider, "S3", StringComparison.OrdinalIgnoreCase))
        {
            var endpoint = configuration["FileStorage:S3:ServiceUrl"];
            var bucket = configuration["FileStorage:S3:BucketName"];
            var key = configuration["FileStorage:S3:AccessKey"];
            status.Configurado = !string.IsNullOrWhiteSpace(endpoint)
                              && !string.IsNullOrWhiteSpace(bucket)
                              && !string.IsNullOrWhiteSpace(key);
            status.DiretorioExiste = status.Configurado;
        }
        else
        {
            status.Configurado = true;
        }

        return status;
    }

    private IaStatus GetIaStatus()
    {
        var enabled = configuration.GetValue<bool>("Anthropic:Enabled");
        var apiKey = configuration["Anthropic:ApiKey"];
        return new IaStatus
        {
            Habilitado = enabled,
            ApiKeyPresente = !string.IsNullOrWhiteSpace(apiKey)
        };
    }

    private ConfiguracoesStatus GetConfiguracoesStatus()
    {
        var jwtKey = configuration["Jwt:SecretKey"];
        var origins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>();
        var cs = configuration.GetConnectionString("DefaultConnection");

        return new ConfiguracoesStatus
        {
            JwtSecretPresente = !string.IsNullOrWhiteSpace(jwtKey),
            JwtSecretSeguro = jwtKey?.Length >= 32,
            CorsOrigins = origins ?? [],
            ConnectionStringPresente = !string.IsNullOrWhiteSpace(cs)
        };
    }

    private static List<CausaProvavel> BuildCausasProvaveis(DiagnosticoResult r)
    {
        var causas = new List<CausaProvavel>();

        if (r.Banco.Conexao == "falha")
            causas.Add(new CausaProvavel
            {
                Componente = "Banco de Dados",
                Severidade = "critical",
                Descricao = r.Banco.CausaProvavel ?? "Banco de dados inacessível.",
                Sugestao = "Verifique a connection string em ConnectionStrings:DefaultConnection e se o serviço está no ar."
            });

        if (r.Banco.Fallback)
            causas.Add(new CausaProvavel
            {
                Componente = "Banco de Dados",
                Severidade = "warning",
                Descricao = "Operando em modo fallback (SQLite).",
                Sugestao = "O banco principal (PostgreSQL/MongoDB) estava indisponível no startup. Reinicie a API após corrigir a conexão."
            });

        if (!r.Configuracoes.JwtSecretPresente)
            causas.Add(new CausaProvavel
            {
                Componente = "Configuração JWT",
                Severidade = "critical",
                Descricao = "Jwt:SecretKey não configurado.",
                Sugestao = "Defina a variável de ambiente Jwt__SecretKey ou configure appsettings."
            });

        if (r.Configuracoes.JwtSecretSeguro == false)
            causas.Add(new CausaProvavel
            {
                Componente = "Configuração JWT",
                Severidade = "warning",
                Descricao = "Jwt:SecretKey tem menos de 32 caracteres.",
                Sugestao = "Use uma chave com pelo menos 32 caracteres para garantir segurança."
            });

        if (r.Redis.Conexao == "falha")
            causas.Add(new CausaProvavel
            {
                Componente = "Redis",
                Severidade = "warning",
                Descricao = r.Redis.CausaProvavel ?? "Redis configurado mas inacessível.",
                Sugestao = "Redis é opcional. Verifique se o serviço está no ar ou remova a connection string se não for usar."
            });

        if (!r.Configuracoes.ConnectionStringPresente)
            causas.Add(new CausaProvavel
            {
                Componente = "Connection String",
                Severidade = "critical",
                Descricao = "Connection string padrão não configurada.",
                Sugestao = "Configure ConnectionStrings:DefaultConnection no appsettings ou via variável de ambiente."
            });

        return causas;
    }

    private static string FormatUptime(TimeSpan ts) =>
        ts.TotalHours >= 1 ? $"{(int)ts.TotalHours}h {ts.Minutes}m" : $"{ts.Minutes}m {ts.Seconds}s";

    // ──────────────────────────────────────────────────────────────────────
    // ParseLogLine usado apenas pelo endpoint /logs (simples)
    // ──────────────────────────────────────────────────────────────────────

    private static readonly Regex _simpleLogLineRegex = new(
        @"^\[(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}) (\w{3})\] (.+)$",
        RegexOptions.Compiled);

    private static readonly Regex[] _simpleSensitivePatterns =
    [
        new Regex(@"(?i)(password|senha|secret|apikey|api_key|token|connectionstring)\s*[=:]\s*\S+", RegexOptions.Compiled),
        new Regex(@"Host=\S+;.*Password=[^;]+", RegexOptions.Compiled),
    ];

    private static LogEntry? ParseLogLine(string line)
    {
        var match = _simpleLogLineRegex.Match(line);
        if (!match.Success) return null;

        var message = match.Groups[3].Value;
        foreach (var pattern in _simpleSensitivePatterns)
            message = pattern.Replace(message, m =>
            {
                var key = m.Value.Split(['=', ':'], 2)[0];
                return $"{key}=[REDACTED]";
            });

        if (!DateTimeOffset.TryParse(match.Groups[1].Value, out var ts)) return null;
        return new LogEntry { Timestamp = ts, Level = DiagnosticoLogAnalyzer.NormalizeLevel(match.Groups[2].Value.ToUpperInvariant()), Message = message };
    }

    private string GetLogDirectory() =>
        configuration[ConfigurationKeys.LogDirectory] is { Length: > 0 } configured
            ? configured
            : Path.Combine(AppContext.BaseDirectory, "logs");

    // ──────────────────────────────────────────────────────────────────────
    // HTML rendering (fallback simples para Accept: text/html)
    // ──────────────────────────────────────────────────────────────────────
    private static string RenderHtml(DiagnosticoResult r, IReadOnlyList<HealthSnapshot> snapshots, EnhancedLogsResult? logs)
    {
        static string Badge(string status) => status switch
        {
            "ok" => "<span class='badge ok'>OK</span>",
            "degraded" => "<span class='badge warn'>DEGRADADO</span>",
            "critical" => "<span class='badge crit'>CRITICO</span>",
            "falha" => "<span class='badge crit'>FALHA</span>",
            "nao_configurado" => "<span class='badge na'>N/C</span>",
            _ => $"<span class='badge'>{status}</span>"
        };

        static string BoolBadge(bool? val) => val switch
        {
            true => "<span class='badge ok'>Sim</span>",
            false => "<span class='badge crit'>Nao</span>",
            null => "<span class='badge na'>N/A</span>"
        };

        static string SevBadge(string sev) => sev switch
        {
            "critical" => "<span class='badge crit'>CRITICO</span>",
            "warning" => "<span class='badge warn'>ALERTA</span>",
            _ => "<span class='badge ok'>INFO</span>"
        };

        var causasHtml = r.CausasProvaveis.Count > 0
            ? "<div class='card alert-card'><h2>&#9888; Causas Provaveis</h2>" +
              string.Join("", r.CausasProvaveis.Select(c =>
                  $"<div class='alert-item'><strong>[{c.Componente}]</strong> {c.Descricao}<br><em>{c.Sugestao}</em></div>")) +
              "</div>"
            : "";

        // Health chart data
        var chartLabels = string.Join(",", snapshots.Select(s => $"\"{s.Timestamp:HH:mm}\""));
        var dbLatencyData = string.Join(",", snapshots.Select(s => s.DbLatencyMs.ToString()));
        var redisLatencyData = string.Join(",", snapshots.Select(s => (s.RedisLatencyMs ?? 0).ToString()));
        var errorData = string.Join(",", snapshots.Select(s => s.ErrorCount.ToString()));
        var dbStatusColors = string.Join(",", snapshots.Select(s =>
            s.DbStatus == "ok" ? "'rgba(22,163,74,0.7)'" : "'rgba(220,38,38,0.7)'"));

        // Logs summary chart data
        var reqByHourLabels = "";
        var reqByHourData = "";
        var errByHourData = "";
        if (logs?.Disponivel == true)
        {
            var hours = Enumerable.Range(0, 24).Select(h => h.ToString("D2")).ToArray();
            reqByHourLabels = string.Join(",", hours.Select(h => $"\"{h}h\""));
            reqByHourData = string.Join(",", hours.Select(h =>
                logs.Resumo.RequestsByHour.TryGetValue(h, out var v) ? v.ToString() : "0"));
            errByHourData = string.Join(",", hours.Select(h =>
                logs.Resumo.ErrorsByHour.TryGetValue(h, out var v) ? v.ToString() : "0"));
        }

        // Patterns HTML
        var patternsHtml = "";
        if (logs?.Padroes.Length > 0)
        {
            patternsHtml = "<div class='card'><h2>&#128270; Padroes Detectados</h2><div class='patterns-list'>" +
                string.Join("", logs.Padroes.Select(p =>
                    $"<div class='pattern-item'>{SevBadge(p.Severidade)} <strong>{p.Tipo}</strong> " +
                    $"<span class='pattern-count'>({p.Ocorrencias}x)</span><br>" +
                    $"<span class='pattern-desc'>{p.Descricao}</span><br>" +
                    $"<em class='pattern-tip'>{p.Sugestao}</em>" +
                    (p.UltimaOcorrencia.HasValue ? $"<br><small>Ultima: {p.UltimaOcorrencia:HH:mm:ss}</small>" : "") +
                    "</div>")) +
                "</div></div>";
        }

        // Log entries HTML
        var logEntriesHtml = "";
        if (logs?.Disponivel == true && logs.Entradas.Length > 0)
        {
            var rows = logs.Entradas.TakeLast(200).Reverse().Select(e =>
            {
                var levelClass = e.Level switch
                {
                    "ERROR" or "FATAL" => "log-error",
                    "WARN" => "log-warn",
                    "DEBUG" => "log-debug",
                    _ => "log-info"
                };
                var cat = e.Categoria switch
                {
                    "http_request" => $"<span class='log-cat cat-http'>{e.HttpMethod} {e.StatusCode}</span>",
                    "migration" => "<span class='log-cat cat-migration'>MIGRATION</span>",
                    "startup" => "<span class='log-cat cat-startup'>STARTUP</span>",
                    "error" => "<span class='log-cat cat-error'>ERROR</span>",
                    "db_operation" => "<span class='log-cat cat-db'>DB</span>",
                    _ => ""
                };
                var elapsed = e.ElapsedMs.HasValue ? $"<span class='log-elapsed'>{e.ElapsedMs:F0}ms</span>" : "";
                var msg = System.Net.WebUtility.HtmlEncode(e.Message.Length > 500 ? e.Message[..500] + "..." : e.Message);
                var exc = e.Exception != null ? $"<div class='log-exception'>{System.Net.WebUtility.HtmlEncode(e.Exception)}</div>" : "";
                return $"<div class='log-row {levelClass}' data-level='{e.Level}' data-cat='{e.Categoria}'>" +
                       $"<span class='log-time'>{e.Timestamp:HH:mm:ss}</span>" +
                       $"<span class='log-level'>{e.Level}</span>" +
                       $"{cat}{elapsed}" +
                       $"<span class='log-msg'>{msg}</span>{exc}</div>";
            });
            logEntriesHtml = string.Join("\n", rows);
        }

        // Log summary stats
        var logStatsHtml = "";
        if (logs?.Disponivel == true)
        {
            logStatsHtml = $"""
                <div class="stats-grid">
                    <div class="stat-box"><div class="stat-num">{logs.TotalEntries}</div><div class="stat-label">Total Entradas</div></div>
                    <div class="stat-box"><div class="stat-num">{logs.Resumo.TotalRequests}</div><div class="stat-label">Requests HTTP</div></div>
                    <div class="stat-box err"><div class="stat-num">{logs.Resumo.TotalErrors}</div><div class="stat-label">Erros</div></div>
                    <div class="stat-box warn"><div class="stat-num">{logs.Resumo.TotalWarnings}</div><div class="stat-label">Warnings</div></div>
                    <div class="stat-box"><div class="stat-num">{logs.Resumo.AvgResponseTimeMs:F0}ms</div><div class="stat-label">Tempo Medio</div></div>
                </div>
                """;
        }

        return $$"""
        <!DOCTYPE html>
        <html lang="pt-BR"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
        <title>EasyStock - Central de Diagnostico</title>
        <script src="https://cdn.jsdelivr.net/npm/chart.js@4.4.4/dist/chart.umd.min.js"></script>
        <style>
            *{box-sizing:border-box;margin:0;padding:0}
            body{font-family:system-ui,-apple-system,sans-serif;background:#0f172a;color:#e2e8f0;min-height:100vh}
            .container{max-width:1200px;margin:0 auto;padding:1rem}
            header{display:flex;align-items:center;justify-content:space-between;padding:1rem 0;border-bottom:1px solid #1e293b;margin-bottom:1.5rem;flex-wrap:wrap;gap:.5rem}
            header h1{font-size:1.5rem;color:#f8fafc;display:flex;align-items:center;gap:.5rem}
            header h1::before{content:'';display:inline-block;width:10px;height:10px;border-radius:50%;background:{{(r.Status == "ok" ? "#16a34a" : r.Status == "degraded" ? "#d97706" : "#dc2626")}};box-shadow:0 0 8px {{(r.Status == "ok" ? "#16a34a" : r.Status == "degraded" ? "#d97706" : "#dc2626")}}}
            .meta{color:#94a3b8;font-size:.8rem}
            .tabs{display:flex;gap:.25rem;margin-bottom:1.5rem;border-bottom:2px solid #1e293b;overflow-x:auto}
            .tab{padding:.6rem 1.2rem;cursor:pointer;color:#94a3b8;border:none;background:none;font-size:.85rem;font-weight:500;border-bottom:2px solid transparent;margin-bottom:-2px;white-space:nowrap;transition:all .2s}
            .tab:hover{color:#e2e8f0}.tab.active{color:#38bdf8;border-bottom-color:#38bdf8}
            .panel{display:none}.panel.active{display:block}
            .card{background:#1e293b;border:1px solid #334155;border-radius:.75rem;padding:1.25rem;margin-bottom:1rem}
            .card h2{font-size:1rem;color:#f8fafc;margin-bottom:.75rem;display:flex;align-items:center;gap:.5rem}
            .alert-card{border-color:#92400e;background:#1c1917}
            .alert-item{padding:.5rem 0;border-bottom:1px solid #334155;font-size:.85rem}.alert-item:last-child{border:none}
            .alert-item em{color:#94a3b8;font-size:.8rem}
            table{width:100%;border-collapse:collapse;font-size:.875rem}
            td{padding:.4rem .75rem;border-bottom:1px solid #334155}td:first-child{color:#94a3b8;width:40%}
            .grid-2{display:grid;grid-template-columns:1fr 1fr;gap:1rem}
            .grid-3{display:grid;grid-template-columns:1fr 1fr 1fr;gap:1rem}
            @media(max-width:768px){.grid-2,.grid-3{grid-template-columns:1fr} }
            .badge{padding:.15rem .5rem;border-radius:.25rem;font-size:.75rem;font-weight:600;display:inline-block}
            .badge.ok{background:#052e16;color:#22c55e}.badge.crit{background:#450a0a;color:#ef4444}
            .badge.warn{background:#422006;color:#f59e0b}.badge.na{background:#1e293b;color:#64748b}
            .chart-box{background:#0f172a;border-radius:.5rem;padding:1rem;position:relative;height:220px}
            .stats-grid{display:grid;grid-template-columns:repeat(5,1fr);gap:.75rem;margin-bottom:1rem}
            @media(max-width:768px){.stats-grid{grid-template-columns:repeat(3,1fr)} }
            .stat-box{background:#0f172a;border:1px solid #334155;border-radius:.5rem;padding:.75rem;text-align:center}
            .stat-box.err{border-color:#7f1d1d}.stat-box.warn{border-color:#78350f}
            .stat-num{font-size:1.5rem;font-weight:700;color:#f8fafc}.stat-label{font-size:.7rem;color:#94a3b8;margin-top:.25rem}
            .log-console{background:#020617;border:1px solid #1e293b;border-radius:.5rem;max-height:500px;overflow-y:auto;font-family:'Cascadia Code','Fira Code',monospace;font-size:.75rem}
            .log-controls{display:flex;gap:.5rem;margin-bottom:.75rem;flex-wrap:wrap;align-items:center}
            .log-controls input{background:#1e293b;border:1px solid #334155;color:#e2e8f0;padding:.4rem .75rem;border-radius:.375rem;font-size:.8rem;flex:1;min-width:200px}
            .log-controls button{padding:.4rem .75rem;border:1px solid #334155;background:#1e293b;color:#94a3b8;border-radius:.375rem;cursor:pointer;font-size:.75rem;white-space:nowrap}
            .log-controls button.active{background:#1e40af;border-color:#3b82f6;color:#e2e8f0}
            .log-row{padding:.3rem .75rem;border-bottom:1px solid #0f172a;display:flex;gap:.5rem;align-items:baseline;flex-wrap:wrap}
            .log-row:hover{background:#1e293b}
            .log-error{border-left:3px solid #ef4444}.log-warn{border-left:3px solid #f59e0b}
            .log-info{border-left:3px solid transparent}.log-debug{border-left:3px solid #6b7280;opacity:.7}
            .log-time{color:#64748b;min-width:55px}.log-level{min-width:40px;font-weight:600}
            .log-error .log-level{color:#ef4444}.log-warn .log-level{color:#f59e0b}.log-info .log-level{color:#38bdf8}
            .log-cat{padding:.1rem .4rem;border-radius:.2rem;font-size:.65rem;font-weight:600}
            .cat-http{background:#1e3a5f;color:#60a5fa}.cat-migration{background:#3b0764;color:#c084fc}
            .cat-startup{background:#064e3b;color:#34d399}.cat-error{background:#450a0a;color:#f87171}
            .cat-db{background:#422006;color:#fbbf24}
            .log-elapsed{color:#94a3b8;font-size:.7rem}.log-msg{color:#cbd5e1;flex:1;word-break:break-word}
            .log-exception{color:#fca5a5;font-size:.7rem;padding:.25rem 0 0 60px;white-space:pre-wrap;word-break:break-all}
            .patterns-list{display:flex;flex-direction:column;gap:.75rem}
            .pattern-item{padding:.75rem;background:#0f172a;border-radius:.5rem;font-size:.85rem;border:1px solid #334155}
            .pattern-count{color:#94a3b8;font-size:.8rem}.pattern-desc{color:#cbd5e1}
            .pattern-tip{color:#94a3b8;font-size:.8rem}
            .links{display:flex;gap:1rem;margin-top:1.5rem;padding-top:1rem;border-top:1px solid #1e293b;flex-wrap:wrap}
            .links a{color:#38bdf8;text-decoration:none;font-size:.85rem}.links a:hover{text-decoration:underline}
            .refresh-bar{display:flex;align-items:center;gap:.5rem;font-size:.8rem;color:#94a3b8}
            .refresh-bar label{cursor:pointer;display:flex;align-items:center;gap:.25rem}
            .section-empty{color:#64748b;text-align:center;padding:2rem;font-size:.85rem}
        </style></head><body>
        <div class="container">
        <header>
            <h1>EasyStock - Central de Diagnostico</h1>
            <div>
                <div class="meta">{{r.Timestamp:yyyy-MM-dd HH:mm:ss}} UTC | Uptime: {{r.Uptime}} | {{r.Ambiente}} | v{{r.Versao}}</div>
                <div class="refresh-bar" style="margin-top:.25rem;justify-content:flex-end">
                    <label><input type="checkbox" id="autoRefresh"> Auto-refresh 30s</label>
                    <button class="tab" onclick="location.reload()" style="padding:.2rem .5rem;font-size:.75rem">&#8635; Atualizar</button>
                </div>
            </div>
        </header>

        <div class="tabs">
            <button class="tab active" onclick="showTab('overview')">Visao Geral</button>
            <button class="tab" onclick="showTab('health')">Saude &amp; Graficos</button>
            <button class="tab" onclick="showTab('logs')">Logs 24h</button>
            <button class="tab" onclick="showTab('patterns')">Alertas &amp; Padroes</button>
        </div>

        <!-- OVERVIEW TAB -->
        <div class="panel active" id="tab-overview">
        <div class="overall" style="font-size:1.25rem;margin-bottom:1rem">Status geral: {{Badge(r.Status)}}</div>
        {{causasHtml}}
        <div class="grid-2">
            <div class="card"><h2>&#128451; Banco de Dados</h2><table>
                <tr><td>Provider</td><td>{{r.Banco.Provider}}</td></tr>
                <tr><td>Configurado</td><td>{{r.Banco.ProviderConfigurado}}</td></tr>
                <tr><td>Fallback</td><td>{{BoolBadge(r.Banco.Fallback)}}</td></tr>
                <tr><td>Conexao</td><td>{{Badge(r.Banco.Conexao)}}</td></tr>
                <tr><td>Latencia</td><td>{{r.Banco.LatenciaMs}}ms</td></tr>
                <tr><td>Migrations</td><td>{{BoolBadge(r.Banco.MigrationsAplicadas)}}</td></tr>
                {{(r.Banco.Erro != null ? $"<tr><td>Erro</td><td style='color:#fca5a5;font-size:.8rem'>{System.Net.WebUtility.HtmlEncode(r.Banco.Erro)}</td></tr>" : "")}}
            </table></div>
            <div class="card"><h2>&#9889; Redis</h2><table>
                <tr><td>Configurado</td><td>{{BoolBadge(r.Redis.Configurado)}}</td></tr>
                <tr><td>Conexao</td><td>{{Badge(r.Redis.Conexao)}}</td></tr>
                <tr><td>Latencia</td><td>{{(r.Redis.Configurado ? r.Redis.LatenciaMs + "ms" : "N/A")}}</td></tr>
            </table></div>
            <div class="card"><h2>&#9993; SMTP / Email</h2><table>
                <tr><td>Configurado</td><td>{{BoolBadge(r.Smtp.Configurado)}}</td></tr>
                <tr><td>Tipo</td><td>{{r.Smtp.Tipo}}</td></tr>
                <tr><td>Host</td><td>{{r.Smtp.Host ?? "N/A"}}</td></tr>
            </table></div>
            <div class="card"><h2>&#128193; Storage</h2><table>
                <tr><td>Provider</td><td>{{r.Storage.Provider}}</td></tr>
                <tr><td>Configurado</td><td>{{BoolBadge(r.Storage.Configurado)}}</td></tr>
                <tr><td>{{(r.Storage.Provider == "Local" ? "Diretorio existe" : "Conexao")}}</td><td>{{BoolBadge(r.Storage.DiretorioExiste)}}</td></tr>
            </table></div>
            <div class="card"><h2>&#129302; IA (Anthropic)</h2><table>
                <tr><td>Habilitado</td><td>{{BoolBadge(r.Ia.Habilitado)}}</td></tr>
                <tr><td>API Key presente</td><td>{{BoolBadge(r.Ia.ApiKeyPresente)}}</td></tr>
            </table></div>
            <div class="card"><h2>&#128272; Configuracoes</h2><table>
                <tr><td>JWT Secret</td><td>{{BoolBadge(r.Configuracoes.JwtSecretPresente)}} {{(r.Configuracoes.JwtSecretSeguro == true ? Badge("ok") : Badge("falha"))}}</td></tr>
                <tr><td>Connection String</td><td>{{BoolBadge(r.Configuracoes.ConnectionStringPresente)}}</td></tr>
                <tr><td>CORS Origins</td><td style="font-size:.8rem">{{string.Join(", ", r.Configuracoes.CorsOrigins)}}</td></tr>
            </table></div>
        </div>
        </div>

        <!-- HEALTH TAB -->
        <div class="panel" id="tab-health">
        {{(snapshots.Count > 0 ? $@"
        <div class='stats-grid' style='grid-template-columns:repeat(4,1fr);margin-bottom:1rem'>
            <div class='stat-box'><div class='stat-num' id='kpiDb'>{(snapshots.Count > 0 ? snapshots[^1].DbLatencyMs + "ms" : "—")}</div><div class='stat-label'>DB Latencia (ultimo)</div></div>
            <div class='stat-box {(snapshots.Count > 0 && snapshots[^1].RedisLatencyMs.HasValue ? "" : "")}'><div class='stat-num'>{(snapshots.Count > 0 && snapshots[^1].RedisLatencyMs.HasValue ? snapshots[^1].RedisLatencyMs + "ms" : "N/C")}</div><div class='stat-label'>Redis Latencia (ultimo)</div></div>
            <div class='stat-box {(snapshots.Count > 0 && snapshots[^1].ErrorCount > 0 ? "err" : "")}'><div class='stat-num' id='kpiErr'>{(snapshots.Count > 0 ? snapshots[^1].ErrorCount.ToString() : "—")}</div><div class='stat-label'>Erros (ultimo min)</div></div>
            <div class='stat-box'><div class='stat-num' id='kpiSnap'>{snapshots.Count} snapshots</div><div class='stat-label'>Historico (max 120 = 2h)</div></div>
        </div>
        <div class='card'><h2>&#128200; Latencia do Banco de Dados</h2>
            <div class='chart-box'><canvas id='dbChart'></canvas></div>
        </div>
        <div class='grid-2'>
            <div class='card'><h2>&#128200; Latencia Redis</h2>
                <div class='chart-box'><canvas id='redisChart'></canvas></div>
            </div>
            <div class='card'><h2>&#128200; Erros por Snapshot (por minuto)</h2>
                <div class='chart-box'><canvas id='errChart'></canvas></div>
            </div>
        </div>
        " : @"<div class='card'><div class='section-empty'>
            Aguardando primeiros snapshots de saude (coletados a cada 60s)...<br>
            <small style='color:#475569;margin-top:.5rem;display:block'>Os graficos aparecao automaticamente apos o primeiro minuto de uptime.</small>
        </div></div>")}}

        {{(logs?.Disponivel == true ? $@"
        <div class='card'><h2>&#128200; Volume de Requests e Erros por Hora (48h)</h2>
            <div class='chart-box' style='height:250px'><canvas id='volumeChart'></canvas></div>
        </div>
        " : "")}}
        </div>

        <!-- LOGS TAB -->
        <div class="panel" id="tab-logs">
        {{(logs?.Disponivel == true ? $@"
        {logStatsHtml}
        <div class='card'>
            <h2>&#128466; Console de Logs (ultimas 48h) <span id='liveDot' style='display:none;width:8px;height:8px;border-radius:50%;background:#22c55e;margin-left:6px;vertical-align:middle'></span></h2>
            <div class='log-controls'>
                <input type='text' id='logFilter' placeholder='Filtrar mensagens...' oninput='filterLogs()'>
                <button class='active' onclick='toggleLevel(this,""all"")'>Todos</button>
                <button onclick='toggleLevel(this,""ERROR"")'>Erros</button>
                <button onclick='toggleLevel(this,""WARN"")'>Warnings</button>
                <button onclick='toggleLevel(this,""INFO"")'>Info</button>
                <button onclick='clearLogs()' style='margin-left:auto;background:#334155;color:#f1f5f9'>Limpar</button>
            </div>
            <div class='log-console' id='logConsole'>
                {logEntriesHtml}
            </div>
            <div style='margin-top:.5rem;font-size:.75rem;color:#64748b'>Mostrando ate 200 entradas mais recentes de {logs.TotalEntries} total</div>
        </div>
        " : "<div class='section-empty'>Logs nao disponiveis neste ambiente.</div>")}}
        </div>

        <!-- PATTERNS TAB -->
        <div class="panel" id="tab-patterns">
        {{(patternsHtml.Length > 0 ? patternsHtml : "<div class='card' style='border-color:#052e16'><div class='section-empty' style='color:#16a34a'>&#10003; Nenhum padrao anomalo detectado nas ultimas 48h.</div></div>")}}

        {{(logs?.Disponivel == true ? $@"
        <div class='grid-2'>
        " + (logs.Resumo.ErrorsByEndpoint.Count > 0 ?
            "<div class='card'><h2>&#128680; Top Endpoints com Erros</h2><table>" +
            string.Join("", logs.Resumo.ErrorsByEndpoint.OrderByDescending(kv => kv.Value).Take(10).Select(kv =>
                $"<tr><td style='font-family:monospace;font-size:.8rem'>{System.Net.WebUtility.HtmlEncode(kv.Key)}</td><td><span class='badge crit'>{kv.Value}x</span></td></tr>")) +
            "</table></div>"
            : "<div class='card'><h2>&#128680; Erros por Endpoint</h2><div class='section-empty'>Sem erros registrados nos endpoints.</div></div>")
        + (logs.Resumo.TotalRequests > 0 && logs.Resumo.AvgResponseTimeMs > 0 ?
            $"<div class='card'><h2>&#9201; Performance (48h)</h2><table>" +
            $"<tr><td>Total Requests</td><td><strong>{logs.Resumo.TotalRequests}</strong></td></tr>" +
            $"<tr><td>Tempo Medio Resposta</td><td><strong style='color:{(logs.Resumo.AvgResponseTimeMs < 200 ? "#22c55e" : logs.Resumo.AvgResponseTimeMs < 1000 ? "#f59e0b" : "#ef4444")}'>{logs.Resumo.AvgResponseTimeMs:F0}ms</strong></td></tr>" +
            $"<tr><td>Taxa de Erro</td><td><strong style='color:{(logs.Resumo.TotalErrors == 0 ? "#22c55e" : "#ef4444")}'>{(logs.Resumo.TotalRequests > 0 ? (100.0 * logs.Resumo.TotalErrors / logs.Resumo.TotalRequests):0):F1}%</strong></td></tr>" +
            $"<tr><td>Warnings</td><td><strong>{logs.Resumo.TotalWarnings}</strong></td></tr>" +
            "</table></div>"
            : "")
        + @"</div>" : "")}}

        <!-- Snapshot health summary -->
        {{(snapshots.Count > 0 ? $@"
        <div class='card'><h2>&#128308; Saude do Banco — Historico de Status</h2>
        <div style='display:flex;flex-wrap:wrap;gap:3px;margin-top:.5rem'>
        {string.Join("", snapshots.TakeLast(60).Select(s => {
            var color = s.DbStatus == "ok" ? "#16a34a" : "#dc2626";
            var title = $"{s.Timestamp:HH:mm} — DB:{s.DbStatus} {s.DbLatencyMs}ms Erros:{s.ErrorCount}";
            return $"<span title='{System.Net.WebUtility.HtmlEncode(title)}' style='display:inline-block;width:10px;height:20px;border-radius:2px;background:{color};cursor:help'></span>";
        }))}
        </div>
        <div style='font-size:.7rem;color:#64748b;margin-top:.4rem'>Cada bloco = 1 minuto. Verde=OK, Vermelho=Falha. Ultimos {Math.Min(snapshots.Count, 60)} minutos.</div>
        </div>" : "")}}
        </div>

        <div class="links">
            <a href="/diagnostico">&#8635; Atualizar</a>
            <a href="/swagger">Swagger</a>
            <a href="/health">Health</a>
            <a href="/health/ready">Readiness</a>
            <a href="/api/diagnostico">JSON</a>
            <a href="/api/diagnostico/historico">Historico JSON</a>
            <a href="/api/diagnostico/endpoints">Teste Endpoints</a>
        </div>
        </div>

        <script>
        let _liveTimer=null;
        let _lastPollTime=new Date().toISOString();

        function showTab(name){
            document.querySelectorAll('.panel').forEach(p=>p.classList.remove('active'));
            document.querySelectorAll('.tab').forEach(t=>t.classList.remove('active'));
            document.getElementById('tab-'+name).classList.add('active');
            event.target.classList.add('active');
            if(name==='health'){setTimeout(initHealthCharts,50);}
            if(name==='logs')startLiveLogs();
            else stopLiveLogs();
        }
        // Auto-init health charts if health tab is loaded directly
        document.addEventListener('DOMContentLoaded',function(){
            if(document.getElementById('tab-health')&&document.getElementById('tab-health').classList.contains('active')){
                setTimeout(initHealthCharts,100);
            }
        });

        function startLiveLogs(){
            if(_liveTimer)return;
            const dot=document.getElementById('liveDot');
            if(dot)dot.style.display='inline-block';
            _liveTimer=setInterval(pollLiveLogs,5000);
        }
        function stopLiveLogs(){
            if(_liveTimer){clearInterval(_liveTimer);_liveTimer=null;}
            const dot=document.getElementById('liveDot');
            if(dot)dot.style.display='none';
        }
        async function pollLiveLogs(){
            try{
                const r=await fetch('/api/diagnostico/logs/live?since='+encodeURIComponent(_lastPollTime));
                if(!r.ok)return;
                const d=await r.json();
                if(d.count>0){
                    const console=document.getElementById('logConsole');
                    if(console){
                        const tmp=document.createElement('div');
                        tmp.innerHTML=d.rows.join('');
                        Array.from(tmp.children).reverse().forEach(el=>console.prepend(el));
                        filterLogs();
                    }
                }
                _lastPollTime=new Date().toISOString();
            }catch{}
        }
        function clearLogs(){
            const c=document.getElementById('logConsole');
            if(c)c.innerHTML='';
            _lastPollTime=new Date().toISOString();
        }

        // Auto-refresh
        document.getElementById('autoRefresh').addEventListener('change',function(){
            if(this.checked){this._timer=setInterval(()=>location.reload(),30000)}
            else{clearInterval(this._timer)}
        });

        // Live-refresh health charts every 60s without full reload
        setInterval(function(){
            fetch('/api/diagnostico/historico').then(r=>r.ok?r.json():null).then(d=>{
                if(!d||!d.snapshots||!d.snapshots.length)return;
                var s=d.snapshots;
                cLabels=s.map(x=>new Date(x.timestamp).toLocaleTimeString('pt-BR',{hour:'2-digit',minute:'2-digit'}));
                dbData=s.map(x=>x.dbLatencyMs>=0?x.dbLatencyMs:null);
                redisData=s.map(x=>x.redisLatencyMs!=null?x.redisLatencyMs:null);
                errData=s.map(x=>x.errorCount);
                // Update KPIs
                var last=s[s.length-1];
                var kpiDb=document.getElementById('kpiDb');var kpiErr=document.getElementById('kpiErr');
                var kpiSnap=document.getElementById('kpiSnap');
                if(kpiDb)kpiDb.textContent=last.dbLatencyMs+'ms';
                if(kpiErr)kpiErr.textContent=last.errorCount;
                if(kpiSnap)kpiSnap.textContent=d.total+' snapshots';
                // Re-render charts if visible
                if(document.getElementById('tab-health').classList.contains('active'))initHealthCharts();
            }).catch(()=>{});
        },60000);

        // Log filtering
        let activeLevel='all';
        function toggleLevel(btn,level){
            activeLevel=level;
            document.querySelectorAll('.log-controls button').forEach(b=>b.classList.remove('active'));
            btn.classList.add('active');
            filterLogs();
        }
        function filterLogs(){
            const q=(document.getElementById('logFilter')?.value||'').toLowerCase();
            document.querySelectorAll('.log-row').forEach(row=>{
                const matchLevel=activeLevel==='all'||row.dataset.level===activeLevel;
                const matchText=!q||row.textContent.toLowerCase().includes(q);
                row.style.display=matchLevel&&matchText?'':'none';
            });
        }

        // Charts
        {{(snapshots.Count > 0 ? "var cLabels=[" + chartLabels + "];" +
            "var dbData=[" + dbLatencyData + "];" +
            "var redisData=[" + redisLatencyData + "];" +
            "var errData=[" + errorData + "];" : "")}}
        {{(logs?.Disponivel == true ? "var volLabels=[" + reqByHourLabels + "];" +
            "var reqData=[" + reqByHourData + "];" +
            "var errHData=[" + errByHourData + "];" : "")}}
        </script>
        <script>
        var CO={responsive:true,maintainAspectRatio:false,plugins:{legend:{display:false} },
            scales:{x:{ticks:{color:'#64748b',maxTicksLimit:15,font:{size:10} },grid:{color:'#1e293b'} },
                    y:{beginAtZero:true,suggestedMax:50,ticks:{color:'#64748b',font:{size:10},callback:function(v){return v+'ms'} },grid:{color:'#1e293b'} } } };
        var COz={responsive:true,maintainAspectRatio:false,plugins:{legend:{display:false} },
            scales:{x:{ticks:{color:'#64748b',maxTicksLimit:15,font:{size:10} },grid:{color:'#1e293b'} },
                    y:{beginAtZero:true,suggestedMax:1,ticks:{color:'#64748b',font:{size:10},stepSize:1},grid:{color:'#1e293b'} } } };
        var COzL={responsive:true,maintainAspectRatio:false,plugins:{legend:{display:true,labels:{color:'#94a3b8',font:{size:11} } } },
            scales:{x:{ticks:{color:'#64748b',maxTicksLimit:24,font:{size:10} },grid:{color:'#1e293b'} },
                    y:{beginAtZero:true,suggestedMax:5,ticks:{color:'#64748b',font:{size:10} },grid:{color:'#1e293b'} } } };

        var _dbChart=null,_redisChart=null,_errChart=null,_volChart=null;
        function initHealthCharts(){
            if(typeof cLabels==='undefined')return;
            if(_dbChart){_dbChart.data.labels=cLabels;_dbChart.data.datasets[0].data=dbData;_dbChart.update();
                _redisChart.data.labels=cLabels;_redisChart.data.datasets[0].data=redisData;_redisChart.update();
                _errChart.data.labels=cLabels;_errChart.data.datasets[0].data=errData;_errChart.update();return;}
            var dbOpts=JSON.parse(JSON.stringify(CO));
            var maxDb=Math.max(...dbData.filter(v=>v>0));
            if(maxDb>0)dbOpts.scales.y.suggestedMax=Math.ceil(maxDb*1.3);
            _dbChart=new Chart(document.getElementById('dbChart'),{type:'line',data:{labels:cLabels,
                datasets:[{label:'DB Latencia (ms)',data:dbData,borderColor:'#38bdf8',backgroundColor:'rgba(56,189,248,0.1)',
                    fill:true,tension:.3,pointRadius:2,borderWidth:2,spanGaps:true}]},options:dbOpts});
            _redisChart=new Chart(document.getElementById('redisChart'),{type:'line',data:{labels:cLabels,
                datasets:[{label:'Redis (ms)',data:redisData,borderColor:'#a78bfa',backgroundColor:'rgba(167,139,250,0.1)',
                    fill:true,tension:.3,pointRadius:2,borderWidth:2,spanGaps:true}]},options:JSON.parse(JSON.stringify(CO))});
            _errChart=new Chart(document.getElementById('errChart'),{type:'bar',data:{labels:cLabels,
                datasets:[{label:'Erros',data:errData,backgroundColor:'rgba(239,68,68,0.6)',borderColor:'#ef4444',borderWidth:1,borderRadius:2}]},options:COz});
            if(typeof volLabels!=='undefined'&&document.getElementById('volumeChart')){
                _volChart=new Chart(document.getElementById('volumeChart'),{type:'bar',data:{labels:volLabels,
                    datasets:[
                        {label:'Requests',data:reqData,backgroundColor:'rgba(56,189,248,0.5)',borderColor:'#38bdf8',borderWidth:1,borderRadius:2},
                        {label:'Erros',data:errHData,backgroundColor:'rgba(239,68,68,0.7)',borderColor:'#ef4444',borderWidth:1,borderRadius:2}
                    ]},options:COzL});
            }
        }
        </script>
        </body></html>
        """;
    }
}

// ──────────────────────────────────────────────────────────────────────────
// DTOs — Existentes
// ──────────────────────────────────────────────────────────────────────────

public sealed class DiagnosticoResult
{
    public string Status { get; set; } = "ok";
    public DateTimeOffset Timestamp { get; set; }
    public string Ambiente { get; set; } = "";
    public string Uptime { get; set; } = "";
    public string Versao { get; set; } = "";
    public BancoStatus Banco { get; set; } = new();
    public RedisStatus Redis { get; set; } = new();
    public SmtpStatus Smtp { get; set; } = new();
    public StorageStatus Storage { get; set; } = new();
    public IaStatus Ia { get; set; } = new();
    public ConfiguracoesStatus Configuracoes { get; set; } = new();
    public List<CausaProvavel> CausasProvaveis { get; set; } = [];
}

public sealed class BancoStatus
{
    public string Provider { get; set; } = "";
    public string ProviderConfigurado { get; set; } = "";
    public bool Fallback { get; set; }
    public string Conexao { get; set; } = "ok";
    public bool? MigrationsAplicadas { get; set; }
    public string? Erro { get; set; }
    public long LatenciaMs { get; set; }
    public string? CausaProvavel { get; set; }
}

public sealed class RedisStatus
{
    public bool Configurado { get; set; }
    public string Conexao { get; set; } = "ok";
    public string? Erro { get; set; }
    public long LatenciaMs { get; set; }
    public string? CausaProvavel { get; set; }
}

public sealed class SmtpStatus
{
    public bool Configurado { get; set; }
    public string Tipo { get; set; } = "";
    public string? Host { get; set; }
}

public sealed class StorageStatus
{
    public string Provider { get; set; } = "";
    public bool Configurado { get; set; }
    public bool? DiretorioExiste { get; set; }
    public string? Erro { get; set; }
}

public sealed class IaStatus
{
    public bool Habilitado { get; set; }
    public bool ApiKeyPresente { get; set; }
}

public sealed class ConfiguracoesStatus
{
    public bool JwtSecretPresente { get; set; }
    public bool? JwtSecretSeguro { get; set; }
    public string[] CorsOrigins { get; set; } = [];
    public bool ConnectionStringPresente { get; set; }
}

public sealed class CausaProvavel
{
    public string Componente { get; set; } = "";
    public string Severidade { get; set; } = "warning";
    public string Descricao { get; set; } = "";
    public string Sugestao { get; set; } = "";
}

public sealed class LogsInfo
{
    public bool Disponivel { get; set; }
    public string? Motivo { get; set; }
    public string? Arquivo { get; set; }
    public int TotalLinhas { get; set; }
    public LogEntry[] Entradas { get; set; } = [];
}

public sealed class LogEntry
{
    public DateTimeOffset Timestamp { get; set; }
    public string Level { get; set; } = "";
    public string Message { get; set; } = "";
}

// ──────────────────────────────────────────────────────────────────────────
// DTOs — Central de Operações Inteligente
// ──────────────────────────────────────────────────────────────────────────

public sealed class EnhancedLogsResult
{
    public bool Disponivel { get; set; }
    public string? Motivo { get; set; }
    public DateTimeOffset QueryTimestamp { get; set; }
    public int PeriodoHoras { get; set; }
    public int TotalEntries { get; set; }
    public EnhancedLogEntry[] Entradas { get; set; } = [];
    public LogSummary Resumo { get; set; } = new();
    public DetectedPattern[] Padroes { get; set; } = [];
}

public sealed class EnhancedLogEntry
{
    public DateTimeOffset Timestamp { get; set; }
    public string Level { get; set; } = "";
    public string Message { get; set; } = "";
    public string? CorrelationId { get; set; }
    public string? Endpoint { get; set; }
    public string? HttpMethod { get; set; }
    public int? StatusCode { get; set; }
    public double? ElapsedMs { get; set; }
    public string? Exception { get; set; }
    public string Categoria { get; set; } = "general";
}

public sealed class LogSummary
{
    public int TotalRequests { get; set; }
    public int TotalErrors { get; set; }
    public int TotalWarnings { get; set; }
    public double AvgResponseTimeMs { get; set; }
    public Dictionary<string, int> ErrorsByEndpoint { get; set; } = new();
    public Dictionary<string, int> RequestsByHour { get; set; } = new();
    public Dictionary<string, int> ErrorsByHour { get; set; } = new();
}

public sealed class DetectedPattern
{
    public string Tipo { get; set; } = "";
    public string Severidade { get; set; } = "info";
    public string Descricao { get; set; } = "";
    public string Sugestao { get; set; } = "";
    public int Ocorrencias { get; set; }
    public DateTimeOffset? PrimeiraOcorrencia { get; set; }
    public DateTimeOffset? UltimaOcorrencia { get; set; }
    /// <summary>ID estável para ack de alertas (SHA256 truncado).</summary>
    public string AlertaId { get; set; } = "";
}

public sealed class EndpointTestResult
{
    public string Rota { get; set; } = "";
    public string Metodo { get; set; } = "GET";
    public int StatusCode { get; set; }
    public long LatenciaMs { get; set; }
    public string Status { get; set; } = "ok";
    public string? Erro { get; set; }
    public DateTimeOffset TestadoEm { get; set; }
}

public sealed class EndpointsTestResponse
{
    public EndpointTestResult[] Resultados { get; set; } = [];
    public int Saudaveis { get; set; }
    public int Lentos { get; set; }
    public int Falhas { get; set; }
    public DateTimeOffset TestadoEm { get; set; }
}

public sealed class HealthHistoryResponse
{
    public HealthSnapshot[] Snapshots { get; set; } = [];
    public DateTimeOffset Desde { get; set; }
    public int Total { get; set; }
}

// ── DTOs novos ────────────────────────────────────────────────────────────────

public sealed class AckAlertaRequest
{
    public string Status { get; set; } = "";
    public string? Observacao { get; set; }
}

public sealed class AlertaAck
{
    public string AlertaId { get; set; } = "";
    public string Status { get; set; } = "";
    public string? Observacao { get; set; }
    public DateTimeOffset AtualizadoEm { get; set; }
}
