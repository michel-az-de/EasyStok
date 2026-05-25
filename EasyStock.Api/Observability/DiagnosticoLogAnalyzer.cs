using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using EasyStock.Api.Controllers;

namespace EasyStock.Api.Observability;

/// <summary>
/// Utilitário estático para parsing e análise dos arquivos de log do EasyStock.
/// Centraliza a lógica que era privada no DiagnosticoController para ser reutilizada
/// por background jobs (DiagnosticoEmailReportJob) e outros consumidores.
/// </summary>
public static class DiagnosticoLogAnalyzer
{
    // ── Regexes compilados ──────────────────────────────────────────────────

    // Format: [2025-01-15 14:32:01 INF] message {Properties:j}
    private static readonly Regex LogLineRegex = new(
        @"^\[(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}) (\w{3})\] (.+)$",
        RegexOptions.Compiled);

    // HTTP GET /api/produtos responded 200 in 12.3456 ms
    private static readonly Regex HttpRequestRegex = new(
        @"HTTP (\w+) (\S+) responded (\d+) in ([\d.]+) ms",
        RegexOptions.Compiled);

    private static readonly Regex PropertiesRegex = new(
        @"\s*\{[^{}]*""CorrelationId""[^{}]*\}\s*$",
        RegexOptions.Compiled);

    private static readonly Regex CorrelationIdRegex = new(
        @"""CorrelationId""\s*:\s*""([^""]+)""",
        RegexOptions.Compiled);

    private static readonly Regex ClientIpRegex = new(
        @"""ClientIP""\s*:\s*""([^""]+)""",
        RegexOptions.Compiled);

    private static readonly Regex UserIdRegex = new(
        @"""UserId""\s*:\s*""([^""]+)""",
        RegexOptions.Compiled);

    private static readonly Regex EmpresaIdRegex = new(
        @"""EmpresaId""\s*:\s*""([^""]+)""",
        RegexOptions.Compiled);

    private static readonly Regex[] SensitivePatterns =
    [
        new Regex(@"(?i)(password|senha|secret|apikey|api_key|token|connectionstring)\s*[=:]\s*\S+", RegexOptions.Compiled),
        new Regex(@"Host=\S+;.*Password=[^;]+", RegexOptions.Compiled),
    ];

    // ── API pública ─────────────────────────────────────────────────────────

    /// <summary>
    /// Lê e agrega todos os arquivos de log que cobrem o período solicitado.
    /// </summary>
    public static List<EnhancedLogEntry> ParseAllLogFiles(string logsDir, DateTime cutoff, int maxEntries = 5000)
    {
        if (!Directory.Exists(logsDir)) return [];

        var logFiles = new DirectoryInfo(logsDir)
            .GetFiles("easystock-*.log")
            .Where(f => f.LastWriteTimeUtc >= cutoff || f.Name.Contains(DateTime.UtcNow.ToString("yyyyMMdd")))
            .OrderBy(f => f.Name)
            .ToList();

        var allEntries = new List<EnhancedLogEntry>();
        foreach (var file in logFiles)
        {
            allEntries.AddRange(ParseEnhancedLogFile(file.FullName, cutoff));
            if (allEntries.Count > maxEntries)
            {
                allEntries = allEntries.TakeLast(maxEntries).ToList();
                break;
            }
        }
        return allEntries;
    }

    /// <summary>
    /// Faz o parsing estruturado de um arquivo de log individual.
    /// </summary>
    public static List<EnhancedLogEntry> ParseEnhancedLogFile(string filePath, DateTime cutoff)
    {
        var entries = new List<EnhancedLogEntry>();
        var currentEntry = (EnhancedLogEntry?)null;
        var exceptionLines = new List<string>();

        using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(fs);

        while (!reader.EndOfStream)
        {
            var line = reader.ReadLine();
            if (line is null) continue;

            var match = LogLineRegex.Match(line);
            if (match.Success)
            {
                // Flush previous entry
                if (currentEntry is not null)
                {
                    if (exceptionLines.Count > 0)
                        currentEntry.Exception = string.Join("\n", exceptionLines);
                    entries.Add(currentEntry);
                    exceptionLines.Clear();
                }

                if (!DateTime.TryParse(match.Groups[1].Value, out var ts))
                    continue;

                if (ts < cutoff)
                {
                    currentEntry = null;
                    continue;
                }

                var levelStr = NormalizeLevel(match.Groups[2].Value.ToUpperInvariant());
                var rawMessage = match.Groups[3].Value;

                // Extract structured properties (CorrelationId, ClientIP, UserId, EmpresaId)
                string? correlationId = null, clientIp = null, userId = null, empresaId = null;
                var propsMatch = PropertiesRegex.Match(rawMessage);
                if (propsMatch.Success)
                {
                    var propsText = propsMatch.Value;
                    var corrMatch = CorrelationIdRegex.Match(propsText);
                    if (corrMatch.Success)
                        correlationId = corrMatch.Groups[1].Value;
                    var ipMatch = ClientIpRegex.Match(propsText);
                    if (ipMatch.Success)
                        clientIp = ipMatch.Groups[1].Value;
                    var uidMatch = UserIdRegex.Match(propsText);
                    if (uidMatch.Success)
                        userId = uidMatch.Groups[1].Value;
                    var empMatch = EmpresaIdRegex.Match(propsText);
                    if (empMatch.Success)
                        empresaId = empMatch.Groups[1].Value;
                    rawMessage = rawMessage[..propsMatch.Index].TrimEnd();
                }

                // Mask sensitive data
                foreach (var pattern in SensitivePatterns)
                    rawMessage = pattern.Replace(rawMessage, m =>
                    {
                        var key = m.Value.Split(['=', ':'], 2)[0];
                        return $"{key}=[REDACTED]";
                    });

                currentEntry = new EnhancedLogEntry
                {
                    Timestamp = new DateTimeOffset(ts, TimeSpan.Zero),
                    Level = levelStr,
                    Message = rawMessage,
                    CorrelationId = correlationId,
                    ClientIp = clientIp,
                    UserId = userId,
                    EmpresaId = empresaId
                };

                var httpMatch = HttpRequestRegex.Match(rawMessage);
                if (httpMatch.Success)
                {
                    currentEntry.Categoria = "http_request";
                    currentEntry.HttpMethod = httpMatch.Groups[1].Value;
                    currentEntry.Endpoint = httpMatch.Groups[2].Value;
                    if (int.TryParse(httpMatch.Groups[3].Value, out var sc))
                        currentEntry.StatusCode = sc;
                    if (double.TryParse(httpMatch.Groups[4].Value, System.Globalization.CultureInfo.InvariantCulture, out var elapsed))
                        currentEntry.ElapsedMs = elapsed;
                }
                else if (levelStr is "ERROR" or "FATAL")
                    currentEntry.Categoria = "error";
                else if (rawMessage.Contains("Migration", StringComparison.OrdinalIgnoreCase) ||
                         rawMessage.Contains("Migrating", StringComparison.OrdinalIgnoreCase))
                    currentEntry.Categoria = "migration";
                else if (rawMessage.Contains("Application started") ||
                         rawMessage.Contains("Now listening on") ||
                         rawMessage.Contains("Content root path") ||
                         rawMessage.Contains("Hosting environment") ||
                         rawMessage.Contains("iniciado"))
                    currentEntry.Categoria = "startup";
                else if (rawMessage.Contains("CanConnect", StringComparison.OrdinalIgnoreCase) ||
                         rawMessage.Contains("DbContext", StringComparison.OrdinalIgnoreCase) ||
                         rawMessage.Contains("database", StringComparison.OrdinalIgnoreCase))
                    currentEntry.Categoria = "db_operation";
                else
                    currentEntry.Categoria = "general";
            }
            else if (currentEntry is not null)
            {
                exceptionLines.Add(line);
            }
        }

        if (currentEntry is not null)
        {
            if (exceptionLines.Count > 0)
                currentEntry.Exception = string.Join("\n", exceptionLines);
            entries.Add(currentEntry);
        }

        return entries;
    }

    /// <summary>
    /// Constrói o resumo estatístico a partir das entradas de log.
    /// </summary>
    public static LogSummary BuildLogSummary(List<EnhancedLogEntry> entries)
    {
        var httpEntries = entries.Where(e => e.Categoria == "http_request").ToList();

        var errorsByEndpoint = httpEntries
            .Where(e => e.StatusCode >= 500)
            .GroupBy(e => e.Endpoint ?? "unknown")
            .ToDictionary(g => g.Key, g => g.Count());

        var requestsByHour = entries
            .GroupBy(e => e.Timestamp.ToString("HH"))
            .OrderBy(g => g.Key)
            .ToDictionary(g => g.Key, g => g.Count());

        var errorsByHour = entries
            .Where(e => e.Level is "ERROR" or "FATAL")
            .GroupBy(e => e.Timestamp.ToString("HH"))
            .OrderBy(g => g.Key)
            .ToDictionary(g => g.Key, g => g.Count());

        return new LogSummary
        {
            TotalRequests = httpEntries.Count,
            TotalErrors = entries.Count(e => e.Level is "ERROR" or "FATAL"),
            TotalWarnings = entries.Count(e => e.Level == "WARN"),
            AvgResponseTimeMs = httpEntries.Count > 0
                ? Math.Round(httpEntries.Where(e => e.ElapsedMs.HasValue).Average(e => e.ElapsedMs!.Value), 1)
                : 0,
            ErrorsByEndpoint = errorsByEndpoint,
            RequestsByHour = requestsByHour,
            ErrorsByHour = errorsByHour
        };
    }

    /// <summary>
    /// Detecta padrões anômalos nas entradas de log. Inclui regressão de performance entre deploys.
    /// </summary>
    public static List<DetectedPattern> DetectPatterns(List<EnhancedLogEntry> entries, bool isFallback = false)
    {
        var patterns = new List<DetectedPattern>();
        var errorEntries = entries.Where(e => e.Level is "ERROR" or "FATAL").ToList();

        // 1. Erros repetidos: mesma mensagem 3+ vezes
        var errorGroups = errorEntries
            .GroupBy(e => e.Message.Length > 100 ? e.Message[..100] : e.Message)
            .Where(g => g.Count() >= 3);

        foreach (var group in errorGroups)
        {
            var descricao = $"Erro repetido {group.Count()}x: {group.Key[..Math.Min(group.Key.Length, 120)]}";
            patterns.Add(new DetectedPattern
            {
                Tipo = "repeated_error",
                Severidade = "critical",
                Descricao = descricao,
                Sugestao = "Investigue a causa raiz deste erro recorrente. Pode indicar um bug sistemático.",
                Ocorrencias = group.Count(),
                PrimeiraOcorrencia = group.Min(e => e.Timestamp),
                UltimaOcorrencia = group.Max(e => e.Timestamp),
                AlertaId = ComputeAlertaId("repeated_error", descricao)
            });
        }

        // 2. Endpoints lentos: média > 1000ms
        var httpByEndpoint = entries
            .Where(e => e.Categoria == "http_request" && e.ElapsedMs.HasValue)
            .GroupBy(e => e.Endpoint ?? "unknown");

        foreach (var group in httpByEndpoint)
        {
            var avg = group.Average(e => e.ElapsedMs!.Value);
            if (avg > 1000)
            {
                var descricao = $"Endpoint lento: {group.Key} — média de {avg:F0}ms ({group.Count()} requests)";
                patterns.Add(new DetectedPattern
                {
                    Tipo = "slow_endpoint",
                    Severidade = "warning",
                    Descricao = descricao,
                    Sugestao = "Verifique queries N+1, falta de índices ou operações bloqueantes neste endpoint.",
                    Ocorrencias = group.Count(),
                    PrimeiraOcorrencia = group.Min(e => e.Timestamp),
                    UltimaOcorrencia = group.Max(e => e.Timestamp),
                    AlertaId = ComputeAlertaId("slow_endpoint", descricao)
                });
            }
        }

        // 3. Falhas de migration
        var migrationErrors = entries
            .Where(e => e.Categoria == "migration" && e.Level is "ERROR" or "FATAL")
            .ToList();

        if (migrationErrors.Count > 0)
        {
            var descricao = $"Falha de migration detectada ({migrationErrors.Count} erros)";
            patterns.Add(new DetectedPattern
            {
                Tipo = "migration_failure",
                Severidade = "critical",
                Descricao = descricao,
                Sugestao = "Verifique o estado do banco de dados e as migrations pendentes.",
                Ocorrencias = migrationErrors.Count,
                PrimeiraOcorrencia = migrationErrors.Min(e => e.Timestamp),
                UltimaOcorrencia = migrationErrors.Max(e => e.Timestamp),
                AlertaId = ComputeAlertaId("migration_failure", descricao)
            });
        }

        // 4. Deploy/restart
        var startupEntries = entries.Where(e => e.Categoria == "startup").ToList();
        if (startupEntries.Count > 0)
        {
            var restartCount = startupEntries.Select(e => e.Timestamp).Distinct().Count();
            if (restartCount > 1)
            {
                var descricao = $"Detectados {restartCount} reinícios/deploys no período";
                patterns.Add(new DetectedPattern
                {
                    Tipo = "deploy_restart",
                    Severidade = "warning",
                    Descricao = descricao,
                    Sugestao = "Múltiplos reinícios podem indicar crashes, OOM kills ou deploys frequentes.",
                    Ocorrencias = restartCount,
                    PrimeiraOcorrencia = startupEntries.Min(e => e.Timestamp),
                    UltimaOcorrencia = startupEntries.Max(e => e.Timestamp),
                    AlertaId = ComputeAlertaId("deploy_restart", descricao)
                });
            }
            else
            {
                const string descricao = "1 startup detectado no período (deploy ou reinício normal)";
                patterns.Add(new DetectedPattern
                {
                    Tipo = "deploy_restart",
                    Severidade = "info",
                    Descricao = descricao,
                    Sugestao = "Startup único é normal após deploy. Verifique se todos os serviços estão operacionais.",
                    Ocorrencias = 1,
                    PrimeiraOcorrencia = startupEntries.Min(e => e.Timestamp),
                    UltimaOcorrencia = startupEntries.Max(e => e.Timestamp),
                    AlertaId = ComputeAlertaId("deploy_restart", descricao)
                });
            }
        }

        // 5. Picos de erro (bucket de 5 min com >5x a média e ≥3 erros)
        var errorBuckets = errorEntries
            .GroupBy(e => new DateTimeOffset(
                e.Timestamp.Year, e.Timestamp.Month, e.Timestamp.Day,
                e.Timestamp.Hour, e.Timestamp.Minute / 5 * 5, 0, e.Timestamp.Offset))
            .ToDictionary(g => g.Key, g => g.Count());

        if (errorBuckets.Count > 2)
        {
            var avgErrorRate = errorBuckets.Values.Average();
            foreach (var spike in errorBuckets.Where(b => b.Value > avgErrorRate * 5 && b.Value >= 3))
            {
                var descricao = $"Pico de erros: {spike.Value} erros em 5min (média: {avgErrorRate:F1})";
                patterns.Add(new DetectedPattern
                {
                    Tipo = "error_spike",
                    Severidade = "critical",
                    Descricao = descricao,
                    Sugestao = "Investigue o que aconteceu neste período. Pode estar relacionado a deploy, falha de dependência ou pico de tráfego.",
                    Ocorrencias = spike.Value,
                    PrimeiraOcorrencia = spike.Key,
                    UltimaOcorrencia = spike.Key.AddMinutes(5),
                    AlertaId = ComputeAlertaId("error_spike", $"Pico de erros: {spike.Value} erros em 5min")
                });
            }
        }

        // 6. Aviso de configuração (fallback ativo)
        if (isFallback)
        {
            const string descricao = "API operando em modo fallback (SQLite)";
            patterns.Add(new DetectedPattern
            {
                Tipo = "config_warning",
                Severidade = "warning",
                Descricao = descricao,
                Sugestao = "O banco principal estava indisponível no startup. Reinicie a API após corrigir a conexão principal.",
                Ocorrencias = 1,
                AlertaId = ComputeAlertaId("config_warning", descricao)
            });
        }

        // 7. Regressão de performance entre deploys
        var startupTimestamps = entries
            .Where(e => e.Categoria == "startup")
            .Select(e => e.Timestamp)
            .OrderBy(t => t)
            .Distinct()
            .ToList();

        if (startupTimestamps.Count >= 1)
        {
            var lastStartup = startupTimestamps.Last();

            var beforeEntries = entries
                .Where(e => e.Timestamp < lastStartup && e.Categoria == "http_request" && e.ElapsedMs.HasValue)
                .ToList();
            var afterEntries = entries
                .Where(e => e.Timestamp >= lastStartup && e.Categoria == "http_request" && e.ElapsedMs.HasValue)
                .ToList();

            if (beforeEntries.Count >= 10 && afterEntries.Count >= 10)
            {
                var beforeByEndpoint = beforeEntries
                    .GroupBy(e => e.Endpoint ?? "unknown")
                    .ToDictionary(g => g.Key, g => g.Average(e => e.ElapsedMs!.Value));
                var afterByEndpoint = afterEntries
                    .GroupBy(e => e.Endpoint ?? "unknown")
                    .ToDictionary(g => g.Key, g => g.Average(e => e.ElapsedMs!.Value));

                foreach (var (endpoint, afterAvg) in afterByEndpoint)
                {
                    if (!beforeByEndpoint.TryGetValue(endpoint, out var beforeAvg) || beforeAvg < 50) continue;
                    var regressionPct = (afterAvg - beforeAvg) / beforeAvg;
                    if (regressionPct > 0.30)
                    {
                        var descricao = $"Regressão de performance: {endpoint} — {beforeAvg:F0}ms→{afterAvg:F0}ms (+{regressionPct:P0})";
                        patterns.Add(new DetectedPattern
                        {
                            Tipo = "performance_regression",
                            Severidade = "warning",
                            Descricao = descricao,
                            Sugestao = "Endpoint ficou mais lento após o último deploy. Verifique mudanças de código, migrations ou carga de dados.",
                            Ocorrencias = afterEntries.Count(e => e.Endpoint == endpoint),
                            PrimeiraOcorrencia = lastStartup,
                            UltimaOcorrencia = afterEntries.Max(e => e.Timestamp),
                            AlertaId = ComputeAlertaId("performance_regression", descricao)
                        });
                    }
                }
            }
        }

        return patterns;
    }

    /// <summary>
    /// Extrai eventos pontuais (deploys, picos de erro) para overlay na timeline.
    /// </summary>
    public static List<TimelineEvent> ExtractTimelineEvents(List<EnhancedLogEntry> entries)
    {
        var eventos = new List<TimelineEvent>();

        // Deploy/restart events
        var startupEntries = entries
            .Where(e => e.Categoria == "startup")
            .GroupBy(e => new DateTimeOffset(
                e.Timestamp.Year, e.Timestamp.Month, e.Timestamp.Day,
                e.Timestamp.Hour, e.Timestamp.Minute / 2 * 2, 0, e.Timestamp.Offset))
            .Select(g => g.First())
            .OrderBy(e => e.Timestamp);

        foreach (var s in startupEntries)
            eventos.Add(new TimelineEvent(s.Timestamp, "deploy", "Startup detectado", "info"));

        // Error spike events (mesma lógica de DetectPatterns)
        var errorEntries = entries.Where(e => e.Level is "ERROR" or "FATAL").ToList();
        var errorBuckets = errorEntries
            .GroupBy(e => new DateTimeOffset(
                e.Timestamp.Year, e.Timestamp.Month, e.Timestamp.Day,
                e.Timestamp.Hour, e.Timestamp.Minute / 5 * 5, 0, e.Timestamp.Offset))
            .ToDictionary(g => g.Key, g => g.Count());

        if (errorBuckets.Count > 2)
        {
            var avg = errorBuckets.Values.Average();
            foreach (var bucket in errorBuckets.Where(b => b.Value > avg * 5 && b.Value >= 3))
                eventos.Add(new TimelineEvent(bucket.Key, "error_spike",
                    $"Pico: {bucket.Value} erros/5min", "critical"));
        }

        return eventos.OrderBy(e => e.Timestamp).ToList();
    }

    /// <summary>
    /// Computa um ID estável para um padrão detectado (SHA256 truncado a 12 hex chars).
    /// </summary>
    public static string ComputeAlertaId(string tipo, string descricao)
    {
        var key = $"{tipo}|{(descricao.Length > 60 ? descricao[..60] : descricao)}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        return Convert.ToHexString(hash)[..12].ToLowerInvariant();
    }

    // ── Helpers privados ────────────────────────────────────────────────────

    public static string NormalizeLevel(string levelStr) => levelStr switch
    {
        "ERR" => "ERROR",
        "WRN" => "WARN",
        "INF" => "INFO",
        "DBG" => "DEBUG",
        "VRB" => "VERBOSE",
        "FTL" => "FATAL",
        _ => levelStr
    };
}

// ── Novos DTOs compartilhados ────────────────────────────────────────────────

public sealed record TimelineEvent(
    DateTimeOffset Timestamp,
    string Tipo,
    string Label,
    string Severidade);
