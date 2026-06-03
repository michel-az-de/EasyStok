using System.Text.RegularExpressions;
using EasyStock.Api.Configuration;
using EasyStock.Api.Observability;
using EasyStock.Application.Ports.Output.Storage;
using EasyStock.Infra.Postgre.Data;
using Microsoft.AspNetCore.RateLimiting;

namespace EasyStock.Api.Controllers;

/// <summary>
/// Endpoints de gerenciamento e consulta de logs de arquivo.
/// Separado de <see cref="DiagnosticoController"/> por responsabilidade.
/// </summary>
[ApiController]
[Route("api/diagnostico")]
[Route("diagnostico")]
[Authorize(Policy = "SuperAdmin")]
[ApiExplorerSettings(GroupName = "v1-ptbr")]
public sealed class DiagnosticoLogsController(
    IConfiguration configuration,
    ILogger<DiagnosticoLogsController> logger,
    EasyStockDbContext db,
    DiagnosticoModeService diagMode) : ControllerBase
{
    // ──────────────────────────────────────────────────────────────────────
    // Regex para parsing de log simples (endpoint /logs)
    // ──────────────────────────────────────────────────────────────────────

    private static readonly Regex _simpleLogLineRegex = new(
        @"^\[(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}) (\w{3})\] (.+)$",
        RegexOptions.Compiled);

    private static readonly Regex[] _simpleSensitivePatterns =
    [
        new Regex(@"(?i)(password|senha|secret|apikey|api_key|token|connectionstring)\s*[=:]\s*\S+", RegexOptions.Compiled),
        new Regex(@"Host=\S+;.*Password=[^;]+", RegexOptions.Compiled),
    ];

    // ──────────────────────────────────────────────────────────────────────
    // Endpoints
    // ──────────────────────────────────────────────────────────────────────

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
                Padroes = DiagnosticoLogAnalyzer.DetectPatterns(allEntries).ToArray()
            });
        }
        catch (Exception ex)
        {
            return Ok(new EnhancedLogsResult { Disponivel = false, Motivo = $"Erro ao processar logs: {ex.Message}" });
        }
    }

    /// <summary>
    /// SSE stream de logs em tempo real. O cliente conecta uma única vez e recebe
    /// eventos "log-batch" a cada 3 segundos com apenas os logs novos (delta).
    /// Substitui o polling HTTP que gerava 29+ alertas de slow_endpoint.
    /// </summary>
    [HttpGet("logs/live")]
    public async Task LiveLogs([FromQuery] string? since = null, CancellationToken ct = default)
    {
        var logsDir = GetLogDirectory();

        Response.ContentType  = "text/event-stream";
        Response.Headers["Cache-Control"]     = "no-cache";
        Response.Headers["X-Accel-Buffering"] = "no"; // desabilita buffer do nginx/proxy

        var cursor = DateTimeOffset.TryParse(since, null, System.Globalization.DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed.UtcDateTime
            : DateTime.UtcNow.AddMinutes(-2);

        // Envia comentário keep-alive para o cliente saber que a conexão está viva
        await Response.WriteAsync(": connected\n\n", ct);
        await Response.Body.FlushAsync(ct);

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(3));

        while (!ct.IsCancellationRequested && await timer.WaitForNextTickAsync(ct))
        {
            try
            {
                if (!Directory.Exists(logsDir))
                {
                    await Response.WriteAsync(": no-log-dir\n\n", ct);
                    await Response.Body.FlushAsync(ct);
                    continue;
                }

                var dir        = new DirectoryInfo(logsDir);
                var logFiles   = dir.GetFiles("easystock-*.log")
                    .Where(f => f.LastWriteTimeUtc >= cursor.AddHours(-1))
                    .OrderBy(f => f.Name)
                    .ToList();

                var newEntries = new List<EnhancedLogEntry>();
                foreach (var file in logFiles)
                {
                    newEntries.AddRange(DiagnosticoLogAnalyzer.ParseEnhancedLogFile(file.FullName, cursor));
                    if (newEntries.Count >= 100) break;
                }

                newEntries = newEntries.TakeLast(100).ToList();

                if (newEntries.Count > 0)
                {
                    cursor = newEntries.Max(e => e.Timestamp).UtcDateTime.AddMilliseconds(1);

                    var rows = newEntries.Select(e =>
                    {
                        var levelClass = e.Level switch
                        {
                            "ERROR" or "FATAL" => "log-error",
                            "WARN"             => "log-warn",
                            "DEBUG"            => "log-debug",
                            _                  => "log-info"
                        };
                        var cat = e.Categoria switch
                        {
                            "http_request" => $"<span class='log-cat cat-http'>{e.HttpMethod} {e.StatusCode}</span>",
                            "migration"    => "<span class='log-cat cat-migration'>MIGRATION</span>",
                            "startup"      => "<span class='log-cat cat-startup'>STARTUP</span>",
                            "error"        => "<span class='log-cat cat-error'>ERROR</span>",
                            "db_operation" => "<span class='log-cat cat-db'>DB</span>",
                            _              => ""
                        };
                        var elapsed = e.ElapsedMs.HasValue ? $"<span class='log-elapsed'>{e.ElapsedMs:F0}ms</span>" : "";
                        var msg     = System.Net.WebUtility.HtmlEncode(e.Message.Length > 500 ? e.Message[..500] + "…" : e.Message);
                        var exc     = e.Exception != null ? $"<div class='log-exception'>{System.Net.WebUtility.HtmlEncode(e.Exception)}</div>" : "";
                        return $"<div class='log-row {levelClass}' data-level='{e.Level}' data-cat='{e.Categoria}'>" +
                               $"<span class='log-time'>{e.Timestamp:HH:mm:ss}</span>" +
                               $"<span class='log-level'>{e.Level}</span>" +
                               $"{cat}{elapsed}" +
                               $"<span class='log-msg'>{msg}</span>{exc}</div>";
                    }).ToArray();

                    var json    = System.Text.Json.JsonSerializer.Serialize(new { rows, count = rows.Length, cursor = cursor.ToString("O") });
                    await Response.WriteAsync($"event: log-batch\ndata: {json}\n\n", ct);
                }
                else
                {
                    // keep-alive a cada tick sem dados novos
                    await Response.WriteAsync($": heartbeat {DateTimeOffset.UtcNow:HH:mm:ss}\n\n", ct);
                }

                await Response.Body.FlushAsync(ct);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "SSE logs/live: erro ao ler logs.");
                try { await Response.WriteAsync($": error {ex.GetType().Name}\n\n", ct); await Response.Body.FlushAsync(ct); }
                catch (Exception writeEx) { logger.LogTrace(writeEx, "SSE logs/live: cliente desconectou durante escrita do erro."); }
            }
        }
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

    [HttpPost("logs/expurgar")]
    public IActionResult ExpurgarLogs([FromQuery] int diasManter = 3)
    {
        diasManter = Math.Clamp(diasManter, 1, 30);
        var cutoff = DateTime.UtcNow.AddDays(-diasManter);
        var logsDir = GetLogDirectory();
        var excluidos = 0;
        long bytesLiberados = 0;
        var detalhes = new List<string>();

        if (Directory.Exists(logsDir))
        {
            var logFiles = new DirectoryInfo(logsDir)
                .GetFiles("easystock-*.log")
                .Where(f => f.LastWriteTimeUtc < cutoff)
                .ToList();

            foreach (var f in logFiles)
            {
                try
                {
                    bytesLiberados += f.Length;
                    f.Delete();
                    excluidos++;
                    detalhes.Add($"Excluído: {f.Name} ({f.Length / 1024}KB)");
                }
                catch (Exception ex)
                {
                    detalhes.Add($"Erro ao excluir {f.Name}: {ex.Message}");
                }
            }

            var lixeiraDir = Path.Combine(logsDir, "lixeira");
            if (Directory.Exists(lixeiraDir))
            {
                var trashFiles = new DirectoryInfo(lixeiraDir)
                    .GetFiles()
                    .Where(f => f.LastWriteTimeUtc < cutoff)
                    .ToList();
                foreach (var f in trashFiles)
                {
                    try
                    {
                        bytesLiberados += f.Length;
                        f.Delete();
                        excluidos++;
                        detalhes.Add($"Lixeira excluída: {f.Name}");
                    }
                    catch { /* ignore */ }
                }
            }
        }

        return Ok(new
        {
            success = true,
            arquivosExcluidos = excluidos,
            espacoLiberadoMb = Math.Round(bytesLiberados / (1024.0 * 1024.0), 2),
            diasMantidos = diasManter,
            mensagem = excluidos > 0
                ? $"Expurgados {excluidos} arquivo(s), liberados {bytesLiberados / 1024}KB."
                : $"Nenhum arquivo com mais de {diasManter} dia(s) encontrado.",
            detalhes
        });
    }

    /// <summary>
    /// Busca paginada server-side com filtros — alimenta a tela de Diagnóstico do Admin.
    /// Reusa <see cref="DiagnosticoLogAnalyzer.ParseAllLogFiles"/>; aplica filtros e paginação
    /// em memória. Retorna {items, total, page, pageSize, totalErros, niveis, endpointsTop}.
    /// </summary>
    /// <param name="level">Filtro multi-valor por nível (CSV: ERROR,FATAL,WARN,INFO,DEBUG).</param>
    /// <param name="hours">Janela de tempo em horas (default 24, máx 168 = 7d).</param>
    /// <param name="endpoint">Substring case-insensitive no campo Endpoint.</param>
    /// <param name="cid">CorrelationId exato (sticky filter na UI).</param>
    /// <param name="search">Substring em Message + Exception.</param>
    /// <param name="page">1-based.</param>
    /// <param name="pageSize">Default 50, máx 500.</param>
    [HttpGet("logs/search")]
    public IActionResult LogsSearch(
        [FromQuery] string? level = null,
        [FromQuery] int hours = 24,
        [FromQuery] string? endpoint = null,
        [FromQuery] string? cid = null,
        [FromQuery] string? search = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        hours = Math.Clamp(hours, 1, 168);
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 500);

        var logsDir = GetLogDirectory();
        if (!Directory.Exists(logsDir))
            return Ok(new { items = Array.Empty<object>(), total = 0, page, pageSize, totalErros = 0, disponivel = false, motivo = "Diretório de logs não encontrado." });

        try
        {
            var cutoff = DateTime.UtcNow.AddHours(-hours);
            var allEntries = DiagnosticoLogAnalyzer.ParseAllLogFiles(logsDir, cutoff);

            // Filtros — todos opcionais, AND lógico.
            IEnumerable<EnhancedLogEntry> filtered = allEntries;

            if (!string.IsNullOrWhiteSpace(level))
            {
                var levels = level.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(s => s.ToUpperInvariant())
                    .ToHashSet();
                filtered = filtered.Where(e => levels.Contains(e.Level.ToUpperInvariant()));
            }

            if (!string.IsNullOrWhiteSpace(endpoint))
                filtered = filtered.Where(e => e.Endpoint?.Contains(endpoint, StringComparison.OrdinalIgnoreCase) == true);

            if (!string.IsNullOrWhiteSpace(cid))
                filtered = filtered.Where(e => string.Equals(e.CorrelationId, cid, StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrWhiteSpace(search))
                filtered = filtered.Where(e =>
                    e.Message.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                    (e.Exception?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false));

            var filteredList = filtered
                .OrderByDescending(e => e.Timestamp)
                .ToList();

            var total = filteredList.Count;
            var totalErros = allEntries.Count(e =>
                string.Equals(e.Level, "ERROR", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(e.Level, "FATAL", StringComparison.OrdinalIgnoreCase));

            // Top 30 endpoints únicos pra alimentar datalist da UI (autocomplete).
            var endpointsTop = allEntries
                .Where(e => !string.IsNullOrWhiteSpace(e.Endpoint))
                .GroupBy(e => e.Endpoint!)
                .OrderByDescending(g => g.Count())
                .Take(30)
                .Select(g => g.Key)
                .ToArray();

            var items = filteredList
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToArray();

            return Ok(new
            {
                disponivel = true,
                items,
                total,
                page,
                pageSize,
                totalErros,
                endpointsTop,
                queryTimestamp = DateTimeOffset.UtcNow,
                periodoHoras = hours
            });
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Falha ao buscar logs via /logs/search.");
            return Ok(new { disponivel = false, motivo = $"Erro ao processar logs: {ex.Message}", items = Array.Empty<object>(), total = 0, page, pageSize });
        }
    }

    /// <summary>
    /// Exporta logs. Default = arquivo .log (texto cru concatenado dos arquivos do período).
    /// Com <c>format=json</c>, devolve array de <see cref="EnhancedLogEntry"/> filtrado
    /// (mesmos filtros do /logs/search) — útil pra anexar em ticket.
    /// </summary>
    [HttpGet("logs/exportar")]
    public IActionResult ExportarLogs(
        [FromQuery] int hours = 48,
        [FromQuery] string? format = null,
        [FromQuery] string? level = null,
        [FromQuery] string? endpoint = null,
        [FromQuery] string? cid = null,
        [FromQuery] string? search = null)
    {
        hours = Math.Clamp(hours, 1, 168);
        var logsDir = GetLogDirectory();
        if (!Directory.Exists(logsDir))
            return NotFound(new { error = "Diretório de logs não encontrado." });

        // ── Format JSON (estruturado, com filtros) ─────────────────────────────
        if (string.Equals(format, "json", StringComparison.OrdinalIgnoreCase))
        {
            var cutoff = DateTime.UtcNow.AddHours(-hours);
            var entries = DiagnosticoLogAnalyzer.ParseAllLogFiles(logsDir, cutoff, maxEntries: 50_000);

            IEnumerable<EnhancedLogEntry> filtered = entries;
            if (!string.IsNullOrWhiteSpace(level))
            {
                var levels = level.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(s => s.ToUpperInvariant())
                    .ToHashSet();
                filtered = filtered.Where(e => levels.Contains(e.Level.ToUpperInvariant()));
            }
            if (!string.IsNullOrWhiteSpace(endpoint))
                filtered = filtered.Where(e => e.Endpoint?.Contains(endpoint, StringComparison.OrdinalIgnoreCase) == true);
            if (!string.IsNullOrWhiteSpace(cid))
                filtered = filtered.Where(e => string.Equals(e.CorrelationId, cid, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(search))
                filtered = filtered.Where(e =>
                    e.Message.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                    (e.Exception?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false));

            var arr = filtered.OrderByDescending(e => e.Timestamp).ToArray();
            var json = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new
            {
                exportadoEm = DateTimeOffset.UtcNow,
                periodoHoras = hours,
                filtros = new { level, endpoint, cid, search },
                total = arr.Length,
                items = arr
            }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            var fileName = $"easystock-logs-{DateTime.UtcNow:yyyyMMdd-HHmm}.json";
            return File(json, "application/json; charset=utf-8", fileName);
        }

        // ── Format texto cru (legado) ──────────────────────────────────────────
        var fcutoff = DateTime.UtcNow.AddHours(-hours);
        var arquivos = new DirectoryInfo(logsDir)
            .GetFiles("easystock-*.log")
            .Where(f => f.LastWriteTimeUtc >= fcutoff || f.Name.Contains(DateTime.UtcNow.ToString("yyyyMMdd")))
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
        var fileNameTxt = $"easystock-logs-{DateTime.UtcNow:yyyyMMdd-HHmm}.log";
        return File(bytes, "text/plain; charset=utf-8", fileNameTxt);
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

    [HttpGet("logs/storage")]
    public async Task<IActionResult> ListarLogsStorage([FromServices] IFileStorage fileStorage, CancellationToken ct)
    {
        try
        {
            var arquivos = await fileStorage.ListAsync("logs", ct);
            return Ok(new
            {
                disponivel = true,
                arquivos = arquivos.Select(f => new
                {
                    storageKey = f.StorageKey,
                    nome = f.FileName,
                    tamanhoBytes = f.SizeBytes,
                    dataModificacao = f.LastModified
                }),
                total = arquivos.Count
            });
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Falha ao listar logs no storage.");
            return Ok(new { disponivel = false, motivo = ex.Message, arquivos = Array.Empty<object>(), total = 0 });
        }
    }

    [HttpGet("logs/storage/conteudo")]
    public async Task<IActionResult> CarregarLogStorage([FromServices] IFileStorage fileStorage, [FromQuery] string file, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(file) || file.Length > 200 || file.Contains(".."))
            return BadRequest(new { error = "Parâmetro 'file' inválido." });

        try
        {
            var bytes = await fileStorage.DownloadAsync(file, ct);
            var text = System.Text.Encoding.UTF8.GetString(bytes);

            var tmpPath = Path.Combine(Path.GetTempPath(), $"easystock-storage-{Guid.NewGuid():N}.log");
            try
            {
                await System.IO.File.WriteAllTextAsync(tmpPath, text, ct);
                var cutoff = DateTime.UtcNow.AddDays(-30);
                var entries = DiagnosticoLogAnalyzer.ParseEnhancedLogFile(tmpPath, cutoff);
                var summary = DiagnosticoLogAnalyzer.BuildLogSummary(entries);
                var padroes = DiagnosticoLogAnalyzer.DetectPatterns(entries);

                return Ok(new EnhancedLogsResult
                {
                    Disponivel = true,
                    Motivo = null,
                    QueryTimestamp = DateTimeOffset.UtcNow,
                    PeriodoHoras = 0,
                    TotalEntries = entries.Count,
                    Entradas = entries.Take(500).ToArray(),
                    Resumo = summary,
                    Padroes = padroes.ToArray()
                });
            }
            finally
            {
                try { System.IO.File.Delete(tmpPath); }
                catch (Exception delEx) { logger.LogDebug(delEx, "Falha ao remover arquivo temporario {Path}", tmpPath); }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Falha ao carregar log do storage: {Key}", file);
            return StatusCode(500, new { error = $"Erro ao carregar arquivo: {ex.Message}" });
        }
    }

    // ──────────────────────────────────────────────────────────────────────
    // SystemErrorLog — erros persistidos no banco (DB, não arquivos Serilog)
    // ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Recebe erros originados no frontend (Admin ou Web).
    /// Grava em SystemErrorLog + loga via Serilog (aparece na aba Erros do Diagnóstico).
    /// </summary>
    [HttpPost("frontend-error")]
    [AllowAnonymous]
    [EnableRateLimiting("mobile-anonymous")]
    public async Task<IActionResult> RegistrarFrontendError([FromBody] FrontendErrorRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Message)) return BadRequest(new { error = "message é obrigatório." });

        var source = req.Source is "admin_frontend" or "web_frontend" ? req.Source : "admin_frontend";
        var msg = req.Message[..Math.Min(req.Message.Length, 1500)];

        logger.LogWarning("[FrontendError] Source:{Source} Category:{Category} Url:{Url} | {Message}",
            source, req.Category, req.Url, msg);

        try
        {
            db.SystemErrorLogs.Add(new SystemErrorLog
            {
                Id = Guid.NewGuid(),
                Source = source,
                Level = "error",
                Category = req.Category ?? "js_error",
                Message = msg,
                Details = req.Details?[..Math.Min(req.Details.Length, 7000)],
                CorrelationId = req.CorrelationId,
                Url = req.Url?[..Math.Min(req.Url.Length, 490)],
                CriadoEm = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Falha ao persistir frontend-error no DB.");
        }

        return Ok(new { ok = true });
    }

    /// <summary>Lista paginada de erros do SystemErrorLog (banco).</summary>
    [HttpGet("system-errors")]
    public async Task<IActionResult> ListarSystemErrors(
        [FromQuery] string? source,
        [FromQuery] string? level,
        [FromQuery] int hours = 24,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        hours = Math.Clamp(hours, 1, 720);
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var cutoff = DateTime.UtcNow.AddHours(-hours);
        var query = db.SystemErrorLogs.Where(x => x.CriadoEm >= cutoff);

        if (!string.IsNullOrWhiteSpace(source))
            query = query.Where(x => x.Source == source);
        if (!string.IsNullOrWhiteSpace(level))
            query = query.Where(x => x.Level == level);

        var total = await query.CountAsync();
        var items = await query
            .OrderByDescending(x => x.CriadoEm)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new
            {
                x.Id,
                x.Source,
                x.Level,
                x.Category,
                x.Message,
                x.Details,
                x.CorrelationId,
                x.Url,
                x.AdminEmail,
                x.TenantId,
                x.CriadoEm
            })
            .ToListAsync();

        return Ok(new { total, page, pageSize, items });
    }

    /// <summary>Remove entradas mais antigas que N dias do SystemErrorLog.</summary>
    [HttpPost("system-errors/expurgar")]
    public async Task<IActionResult> ExpurgarSystemErrors([FromQuery] int diasManter = 7)
    {
        diasManter = Math.Clamp(diasManter, 1, 365);
        var cutoff = DateTime.UtcNow.AddDays(-diasManter);
        var deleted = await db.SystemErrorLogs
            .Where(x => x.CriadoEm < cutoff)
            .ExecuteDeleteAsync();
        logger.LogInformation("[SystemErrorLog] Expurgados {Count} registros anteriores a {Cutoff}", deleted, cutoff);
        return Ok(new { deleted, diasManter, cutoff });
    }

    // ──────────────────────────────────────────────────────────────────────
    // Toggle de logging verbose (DiagnosticoModeService)
    // ──────────────────────────────────────────────────────────────────────

    /// <summary>Retorna o estado atual do modo de logging.</summary>
    [HttpGet("logging-mode")]
    public IActionResult GetLoggingMode() =>
        Ok(new
        {
            isVerbose = diagMode.IsVerboseEnabled,
            minimumLevel = diagMode.LevelSwitch.MinimumLevel.ToString(),
            changedAt = diagMode.ChangedAt,
            changedBy = diagMode.ChangedBy
        });

    /// <summary>Altera o modo de logging em tempo real. Persiste no DB.</summary>
    [HttpPost("logging-mode")]
    public async Task<IActionResult> SetLoggingMode([FromBody] LoggingModeRequest req)
    {
        var adminEmail = User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value
                      ?? User.FindFirst("email")?.Value
                      ?? "admin";
        await diagMode.SetVerboseAsync(req.Verbose, adminEmail);
        logger.LogInformation("[DiagnosticoMode] Verbose={Verbose} ativado por {Admin}", req.Verbose, adminEmail);
        return Ok(new { isVerbose = req.Verbose, minimumLevel = diagMode.LevelSwitch.MinimumLevel.ToString() });
    }

    // ──────────────────────────────────────────────────────────────────────
    // Helpers privados
    // ──────────────────────────────────────────────────────────────────────

    private string GetLogDirectory() =>
        configuration[ConfigurationKeys.LogDirectory] is { Length: > 0 } configured
            ? configured
            : Path.Combine(AppContext.BaseDirectory, "logs");

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
}

// ─── Request DTOs ──────────────────────────────────────────────────────────────

public sealed record FrontendErrorRequest(
    string? Source,
    string? Category,
    string Message,
    string? Details,
    string? CorrelationId,
    string? Url);

public sealed record LoggingModeRequest(bool Verbose);
