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
    IHttpClientFactory httpClientFactory) : ControllerBase
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
                var cutoff = DateTime.UtcNow.AddHours(-24);
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
                            allEntries.AddRange(ParseEnhancedLogFile(file.FullName, cutoff));
                            if (allEntries.Count > 5000) { allEntries = allEntries.TakeLast(5000).ToList(); break; }
                        }

                        enhancedLogs = new EnhancedLogsResult
                        {
                            Disponivel = true,
                            QueryTimestamp = DateTimeOffset.UtcNow,
                            PeriodoHoras = 24,
                            TotalEntries = allEntries.Count,
                            Entradas = allEntries.TakeLast(500).ToArray(),
                            Resumo = BuildLogSummary(allEntries),
                            Padroes = DetectPatterns(allEntries).ToArray()
                        };
                    }
                }
            }
            catch { /* logs are best-effort for dashboard */ }

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
    [Authorize(Policy = "Admin")]
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
    [Authorize(Policy = "Admin")]
    public IActionResult EnhancedLogs([FromQuery] int hours = 24)
    {
        hours = Math.Clamp(hours, 1, 72);
        var cutoff = DateTime.UtcNow.AddHours(-hours);

        var logsDir = GetLogDirectory();
        if (!Directory.Exists(logsDir))
        {
            return Ok(new EnhancedLogsResult
            {
                Disponivel = false,
                Motivo = "Diretório de logs não encontrado."
            });
        }

        try
        {
            // Collect log files covering the requested window
            var dir = new DirectoryInfo(logsDir);
            var logFiles = dir.GetFiles("easystock-*.log")
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .Where(f => f.LastWriteTimeUtc >= cutoff)
                .OrderBy(f => f.Name)
                .ToList();

            if (logFiles.Count == 0)
            {
                return Ok(new EnhancedLogsResult
                {
                    Disponivel = false,
                    Motivo = "Nenhum arquivo de log encontrado para o período solicitado."
                });
            }

            var allEntries = new List<EnhancedLogEntry>();

            foreach (var file in logFiles)
            {
                var entries = ParseEnhancedLogFile(file.FullName, cutoff);
                allEntries.AddRange(entries);

                if (allEntries.Count > 5000)
                {
                    allEntries = allEntries.TakeLast(5000).ToList();
                    break;
                }
            }

            // Build summary
            var summary = BuildLogSummary(allEntries);

            // Detect patterns
            var patterns = DetectPatterns(allEntries);

            return Ok(new EnhancedLogsResult
            {
                Disponivel = true,
                QueryTimestamp = DateTimeOffset.UtcNow,
                PeriodoHoras = hours,
                TotalEntries = allEntries.Count,
                Entradas = allEntries.ToArray(),
                Resumo = summary,
                Padroes = patterns.ToArray()
            });
        }
        catch (Exception ex)
        {
            return Ok(new EnhancedLogsResult
            {
                Disponivel = false,
                Motivo = $"Erro ao processar logs: {ex.Message}"
            });
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
            status.Configurado = !string.IsNullOrWhiteSpace(connStr) && !string.IsNullOrWhiteSpace(shareName);
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
                catch
                {
                    status.DiretorioExiste = false;
                    status.Configurado = false;
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
    // Enhanced Log Parsing
    // ──────────────────────────────────────────────────────────────────────

    // Format: [2025-01-15 14:32:01 INF] message {Properties:j}
    private static readonly Regex LogLineRegex = new(
        @"^\[(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}) (\w{3})\] (.+)$",
        RegexOptions.Compiled);

    // HTTP request log from Serilog: HTTP GET /api/produtos responded 200 in 12.3456 ms
    private static readonly Regex HttpRequestRegex = new(
        @"HTTP (\w+) (\S+) responded (\d+) in ([\d.]+) ms",
        RegexOptions.Compiled);

    // Properties JSON block at the end of a message
    private static readonly Regex PropertiesRegex = new(
        @"\s*\{[^{}]*""CorrelationId""[^{}]*\}\s*$",
        RegexOptions.Compiled);

    private static readonly Regex CorrelationIdRegex = new(
        @"""CorrelationId""\s*:\s*""([^""]+)""",
        RegexOptions.Compiled);

    private static readonly Regex[] SensitivePatterns =
    [
        new Regex(@"(?i)(password|senha|secret|apikey|api_key|token|connectionstring)\s*[=:]\s*\S+", RegexOptions.Compiled),
        new Regex(@"Host=\S+;.*Password=[^;]+", RegexOptions.Compiled),
    ];

    private static LogEntry? ParseLogLine(string line)
    {
        var match = LogLineRegex.Match(line);
        if (!match.Success) return null;

        var message = match.Groups[3].Value;

        foreach (var pattern in SensitivePatterns)
            message = pattern.Replace(message, m =>
            {
                var key = m.Value.Split(['=', ':'], 2)[0];
                return $"{key}=[REDACTED]";
            });

        var levelStr = match.Groups[2].Value.ToUpperInvariant();
        var level = NormalizeLevel(levelStr);

        if (!DateTimeOffset.TryParse(match.Groups[1].Value, out var ts))
            return null;

        return new LogEntry { Timestamp = ts, Level = level, Message = message };
    }

    private static string NormalizeLevel(string levelStr) => levelStr switch
    {
        "ERR" => "ERROR",
        "WRN" => "WARN",
        "INF" => "INFO",
        "DBG" => "DEBUG",
        "VRB" => "VERBOSE",
        "FTL" => "FATAL",
        _ => levelStr
    };

    private static List<EnhancedLogEntry> ParseEnhancedLogFile(string filePath, DateTime cutoff)
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

                // Extract CorrelationId from Properties JSON block
                string? correlationId = null;
                var propsMatch = PropertiesRegex.Match(rawMessage);
                if (propsMatch.Success)
                {
                    var corrMatch = CorrelationIdRegex.Match(propsMatch.Value);
                    if (corrMatch.Success)
                        correlationId = corrMatch.Groups[1].Value;
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
                    CorrelationId = correlationId
                };

                // Classify & extract HTTP request data
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
                {
                    currentEntry.Categoria = "error";
                }
                else if (rawMessage.Contains("Migration", StringComparison.OrdinalIgnoreCase) ||
                         rawMessage.Contains("Migrating", StringComparison.OrdinalIgnoreCase))
                {
                    currentEntry.Categoria = "migration";
                }
                else if (rawMessage.Contains("Application started") ||
                         rawMessage.Contains("Now listening on") ||
                         rawMessage.Contains("Content root path") ||
                         rawMessage.Contains("Hosting environment") ||
                         rawMessage.Contains("iniciado"))
                {
                    currentEntry.Categoria = "startup";
                }
                else if (rawMessage.Contains("CanConnect", StringComparison.OrdinalIgnoreCase) ||
                         rawMessage.Contains("DbContext", StringComparison.OrdinalIgnoreCase) ||
                         rawMessage.Contains("database", StringComparison.OrdinalIgnoreCase))
                {
                    currentEntry.Categoria = "db_operation";
                }
                else
                {
                    currentEntry.Categoria = "general";
                }
            }
            else if (currentEntry is not null)
            {
                // Multi-line continuation (exception stack trace)
                exceptionLines.Add(line);
            }
        }

        // Flush last entry
        if (currentEntry is not null)
        {
            if (exceptionLines.Count > 0)
                currentEntry.Exception = string.Join("\n", exceptionLines);
            entries.Add(currentEntry);
        }

        return entries;
    }

    private static LogSummary BuildLogSummary(List<EnhancedLogEntry> entries)
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

    private List<DetectedPattern> DetectPatterns(List<EnhancedLogEntry> entries)
    {
        var patterns = new List<DetectedPattern>();

        // 1. Repeated errors: same message 3+ times within 1 hour window
        var errorEntries = entries.Where(e => e.Level is "ERROR" or "FATAL").ToList();
        var errorGroups = errorEntries
            .GroupBy(e => e.Message.Length > 100 ? e.Message[..100] : e.Message)
            .Where(g => g.Count() >= 3);

        foreach (var group in errorGroups)
        {
            patterns.Add(new DetectedPattern
            {
                Tipo = "repeated_error",
                Severidade = "critical",
                Descricao = $"Erro repetido {group.Count()}x: {group.Key[..Math.Min(group.Key.Length, 120)]}",
                Sugestao = "Investigue a causa raiz deste erro recorrente. Pode indicar um bug sistemático.",
                Ocorrencias = group.Count(),
                PrimeiraOcorrencia = group.Min(e => e.Timestamp),
                UltimaOcorrencia = group.Max(e => e.Timestamp)
            });
        }

        // 2. Slow endpoints: avg > 1000ms
        var httpByEndpoint = entries
            .Where(e => e.Categoria == "http_request" && e.ElapsedMs.HasValue)
            .GroupBy(e => e.Endpoint ?? "unknown");

        foreach (var group in httpByEndpoint)
        {
            var avg = group.Average(e => e.ElapsedMs!.Value);
            if (avg > 1000)
            {
                patterns.Add(new DetectedPattern
                {
                    Tipo = "slow_endpoint",
                    Severidade = "warning",
                    Descricao = $"Endpoint lento: {group.Key} — média de {avg:F0}ms ({group.Count()} requests)",
                    Sugestao = "Verifique queries N+1, falta de índices ou operações bloqueantes neste endpoint.",
                    Ocorrencias = group.Count(),
                    PrimeiraOcorrencia = group.Min(e => e.Timestamp),
                    UltimaOcorrencia = group.Max(e => e.Timestamp)
                });
            }
        }

        // 3. Migration failures
        var migrationErrors = entries
            .Where(e => e.Categoria == "migration" && e.Level is "ERROR" or "FATAL")
            .ToList();

        if (migrationErrors.Count > 0)
        {
            patterns.Add(new DetectedPattern
            {
                Tipo = "migration_failure",
                Severidade = "critical",
                Descricao = $"Falha de migration detectada ({migrationErrors.Count} erros)",
                Sugestao = "Verifique o estado do banco de dados e as migrations pendentes. Execute 'dotnet ef migrations list' para diagnóstico.",
                Ocorrencias = migrationErrors.Count,
                PrimeiraOcorrencia = migrationErrors.Min(e => e.Timestamp),
                UltimaOcorrencia = migrationErrors.Max(e => e.Timestamp)
            });
        }

        // 4. Deploy/restart detection: startup messages after activity gap
        var startupEntries = entries.Where(e => e.Categoria == "startup").ToList();
        if (startupEntries.Count > 0)
        {
            var restartCount = startupEntries
                .Select(e => e.Timestamp)
                .Distinct()
                .Count();

            // More than 1 startup in the window means restarts
            if (restartCount > 1)
            {
                patterns.Add(new DetectedPattern
                {
                    Tipo = "deploy_restart",
                    Severidade = "warning",
                    Descricao = $"Detectados {restartCount} reinícios/deploys no período",
                    Sugestao = "Múltiplos reinícios podem indicar crashes, OOM kills ou deploys frequentes. Verifique logs de startup para erros.",
                    Ocorrencias = restartCount,
                    PrimeiraOcorrencia = startupEntries.Min(e => e.Timestamp),
                    UltimaOcorrencia = startupEntries.Max(e => e.Timestamp)
                });
            }
            else
            {
                patterns.Add(new DetectedPattern
                {
                    Tipo = "deploy_restart",
                    Severidade = "info",
                    Descricao = "1 startup detectado no período (deploy ou reinício normal)",
                    Sugestao = "Startup único é normal após deploy. Verifique se todos os serviços estão operacionais.",
                    Ocorrencias = 1,
                    PrimeiraOcorrencia = startupEntries.Min(e => e.Timestamp),
                    UltimaOcorrencia = startupEntries.Max(e => e.Timestamp)
                });
            }
        }

        // 5. Error spike: any 5-min bucket with >5x the average error rate
        var errorBuckets = errorEntries
            .GroupBy(e => new DateTimeOffset(
                e.Timestamp.Year, e.Timestamp.Month, e.Timestamp.Day,
                e.Timestamp.Hour, e.Timestamp.Minute / 5 * 5, 0, e.Timestamp.Offset))
            .ToDictionary(g => g.Key, g => g.Count());

        if (errorBuckets.Count > 2)
        {
            var avgErrorRate = errorBuckets.Values.Average();
            var spikes = errorBuckets.Where(b => b.Value > avgErrorRate * 5 && b.Value >= 3).ToList();

            foreach (var spike in spikes)
            {
                patterns.Add(new DetectedPattern
                {
                    Tipo = "error_spike",
                    Severidade = "critical",
                    Descricao = $"Pico de erros: {spike.Value} erros em 5min (média: {avgErrorRate:F1})",
                    Sugestao = "Investigue o que aconteceu neste período. Pode estar relacionado a deploy, falha de dependência ou pico de tráfego.",
                    Ocorrencias = spike.Value,
                    PrimeiraOcorrencia = spike.Key,
                    UltimaOcorrencia = spike.Key.AddMinutes(5)
                });
            }
        }

        // 6. Configuration warnings from current state
        if (infraState.IsFallback)
        {
            patterns.Add(new DetectedPattern
            {
                Tipo = "config_warning",
                Severidade = "warning",
                Descricao = "API operando em modo fallback (SQLite)",
                Sugestao = "O banco principal estava indisponível no startup. Reinicie a API após corrigir a conexão principal.",
                Ocorrencias = 1
            });
        }

        return patterns;
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
                var msg = System.Net.WebUtility.HtmlEncode(e.Message.Length > 200 ? e.Message[..200] + "..." : e.Message);
                var exc = e.Exception != null ? $"<div class='log-exception'>{System.Net.WebUtility.HtmlEncode(e.Exception.Length > 300 ? e.Exception[..300] + "..." : e.Exception)}</div>" : "";
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
        <div class='card'><h2>&#128200; Latencia do Banco de Dados (ultimos {snapshots.Count} snapshots)</h2>
            <div class='chart-box'><canvas id='dbChart'></canvas></div>
        </div>
        <div class='grid-2'>
            <div class='card'><h2>&#128200; Latencia Redis</h2>
                <div class='chart-box'><canvas id='redisChart'></canvas></div>
            </div>
            <div class='card'><h2>&#128200; Erros por Snapshot</h2>
                <div class='chart-box'><canvas id='errChart'></canvas></div>
            </div>
        </div>
        " : "<div class='section-empty'>Aguardando snapshots de saude (coletados a cada 60s)...</div>")}}

        {{(logs?.Disponivel == true ? $@"
        <div class='card'><h2>&#128200; Volume por Hora (24h)</h2>
            <div class='chart-box' style='height:250px'><canvas id='volumeChart'></canvas></div>
        </div>
        " : "")}}
        </div>

        <!-- LOGS TAB -->
        <div class="panel" id="tab-logs">
        {{(logs?.Disponivel == true ? $@"
        {logStatsHtml}
        <div class='card'>
            <h2>&#128466; Console de Logs (ultimas 24h)</h2>
            <div class='log-controls'>
                <input type='text' id='logFilter' placeholder='Filtrar mensagens...' oninput='filterLogs()'>
                <button class='active' onclick='toggleLevel(this,""all"")'>Todos</button>
                <button onclick='toggleLevel(this,""ERROR"")'>Erros</button>
                <button onclick='toggleLevel(this,""WARN"")'>Warnings</button>
                <button onclick='toggleLevel(this,""INFO"")'>Info</button>
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
        {{(patternsHtml.Length > 0 ? patternsHtml : "<div class='section-empty'>Nenhum padrao detectado nas ultimas 24h.</div>")}}

        {{(logs?.Disponivel == true && logs.Resumo.ErrorsByEndpoint.Count > 0 ?
            "<div class='card'><h2>&#128680; Erros por Endpoint</h2><table>" +
            string.Join("", logs.Resumo.ErrorsByEndpoint.OrderByDescending(kv => kv.Value).Select(kv =>
                $"<tr><td>{kv.Key}</td><td><span class='badge crit'>{kv.Value} erros</span></td></tr>")) +
            "</table></div>" : "")}}
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
        function showTab(name){
            document.querySelectorAll('.panel').forEach(p=>p.classList.remove('active'));
            document.querySelectorAll('.tab').forEach(t=>t.classList.remove('active'));
            document.getElementById('tab-'+name).classList.add('active');
            event.target.classList.add('active');
            if(name==='health')initHealthCharts();
        }

        // Auto-refresh
        document.getElementById('autoRefresh').addEventListener('change',function(){
            if(this.checked){this._timer=setInterval(()=>location.reload(),30000)}
            else{clearInterval(this._timer)}
        });

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
                    y:{ticks:{color:'#64748b',font:{size:10} },grid:{color:'#1e293b'} } };
        var COz=JSON.parse(JSON.stringify(CO));COz.scales.y.beginAtZero=true;
        var COzL=JSON.parse(JSON.stringify(COz));COzL.plugins.legend={display:true,labels:{color:'#94a3b8',font:{size:11} } };

        var _healthChartsInited=false;
        function initHealthCharts(){
            if(_healthChartsInited)return;
            _healthChartsInited=true;
            if(typeof cLabels!=='undefined'){
                new Chart(document.getElementById('dbChart'),{type:'line',data:{labels:cLabels,
                    datasets:[{label:'DB Latencia (ms)',data:dbData,borderColor:'#38bdf8',backgroundColor:'rgba(56,189,248,0.1)',
                        fill:true,tension:.3,pointRadius:1,borderWidth:2}]},options:CO});
                new Chart(document.getElementById('redisChart'),{type:'line',data:{labels:cLabels,
                    datasets:[{label:'Redis (ms)',data:redisData,borderColor:'#a78bfa',backgroundColor:'rgba(167,139,250,0.1)',
                        fill:true,tension:.3,pointRadius:1,borderWidth:2}]},options:CO});
                new Chart(document.getElementById('errChart'),{type:'bar',data:{labels:cLabels,
                    datasets:[{label:'Erros',data:errData,backgroundColor:'rgba(239,68,68,0.6)',borderColor:'#ef4444',borderWidth:1}]},options:COz});
            }
            if(typeof volLabels!=='undefined'&&document.getElementById('volumeChart')){
                new Chart(document.getElementById('volumeChart'),{type:'bar',data:{labels:volLabels,
                    datasets:[
                        {label:'Requests',data:reqData,backgroundColor:'rgba(56,189,248,0.5)',borderColor:'#38bdf8',borderWidth:1},
                        {label:'Erros',data:errHData,backgroundColor:'rgba(239,68,68,0.6)',borderColor:'#ef4444',borderWidth:1}
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
