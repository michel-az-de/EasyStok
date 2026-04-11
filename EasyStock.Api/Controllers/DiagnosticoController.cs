using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using EasyStock.Api.Observability;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Storage;
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
    IEmailService emailService) : ControllerBase
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
            Storage = GetStorageStatus(),
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
            return Content(RenderHtml(result), "text/html; charset=utf-8");

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

        var logsDir = Path.Combine(AppContext.BaseDirectory, "logs");
        var today = DateTime.UtcNow.ToString("yyyyMMdd");
        var logFile = Path.Combine(logsDir, $"easystock-{today}.log");

        if (!System.IO.File.Exists(logFile))
        {
            // Tenta encontrar o arquivo de log mais recente no diretório
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
            // Leitura com FileShare.ReadWrite para não bloquear Serilog
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
    // Helpers privados
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

        // Teste de conexão em tempo real
        try
        {
            if (infraState.DatabaseProvider is "postgresql" or "sqlite")
            {
                using var scope = HttpContext.RequestServices.CreateScope();
                var dbType = typeof(Microsoft.EntityFrameworkCore.DbContext);
                var dbContext = scope.ServiceProvider.GetServices<object>()
                    .FirstOrDefault(s => s.GetType().IsSubclassOf(dbType) || s.GetType() == dbType);

                if (dbContext is Microsoft.EntityFrameworkCore.DbContext db)
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

    private StorageStatus GetStorageStatus()
    {
        var provider = configuration["FileStorage:Provider"] ?? "Local";
        var status = new StorageStatus { Provider = provider, Configurado = true };

        if (string.Equals(provider, "Local", StringComparison.OrdinalIgnoreCase))
        {
            var rootPath = configuration["FileStorage:LocalRootPath"] ?? "uploaded-files";
            status.DiretorioExiste = Directory.Exists(rootPath);
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
    // Log parsing
    // ──────────────────────────────────────────────────────────────────────

    // Formato: [2025-01-15 14:32:01 INF] mensagem
    private static readonly Regex LogLineRegex = new(
        @"^\[(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}) (\w{3})\] (.+)$",
        RegexOptions.Compiled);

    // Padrões de dados sensíveis a mascarar
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

        // Mascarar dados sensíveis
        foreach (var pattern in SensitivePatterns)
            message = pattern.Replace(message, m =>
            {
                var key = m.Value.Split(['=', ':'], 2)[0];
                return $"{key}=[REDACTED]";
            });

        var levelStr = match.Groups[2].Value.ToUpperInvariant();
        var level = levelStr switch
        {
            "ERR" => "ERROR",
            "WRN" => "WARN",
            "INF" => "INFO",
            "DBG" => "DEBUG",
            "VRB" => "VERBOSE",
            "FTL" => "FATAL",
            _ => levelStr
        };

        if (!DateTimeOffset.TryParse(match.Groups[1].Value, out var ts))
            return null;

        return new LogEntry { Timestamp = ts, Level = level, Message = message };
    }

    // ──────────────────────────────────────────────────────────────────────
    // HTML rendering (fallback simples para Accept: text/html)
    // ──────────────────────────────────────────────────────────────────────
    private static string RenderHtml(DiagnosticoResult r)
    {
        static string Badge(string status) => status switch
        {
            "ok" => "<span style='color:#16a34a;font-weight:bold'>OK</span>",
            "degraded" => "<span style='color:#d97706;font-weight:bold'>DEGRADADO</span>",
            "critical" => "<span style='color:#dc2626;font-weight:bold'>CRITICO</span>",
            "falha" => "<span style='color:#dc2626;font-weight:bold'>FALHA</span>",
            "nao_configurado" => "<span style='color:#6b7280'>Nao configurado</span>",
            _ => $"<span>{status}</span>"
        };

        static string BoolBadge(bool? val) => val switch
        {
            true => "<span style='color:#16a34a'>Sim</span>",
            false => "<span style='color:#dc2626'>Nao</span>",
            null => "<span style='color:#6b7280'>N/A</span>"
        };

        var causasHtml = r.CausasProvaveis.Count > 0
            ? "<div class='card'><h2>⚠️ Causas Prováveis</h2>" +
              string.Join("", r.CausasProvaveis.Select(c =>
                  $"<p><strong>[{c.Componente}]</strong> {c.Descricao}<br><em>{c.Sugestao}</em></p>")) +
              "</div>"
            : "";

        return $$"""
        <!DOCTYPE html>
        <html><head><meta charset="utf-8"><title>Diagnostico EasyStock</title>
        <style>
            body{font-family:system-ui,sans-serif;max-width:800px;margin:2rem auto;padding:0 1rem;background:#f8fafc;color:#1e293b}
            h1{margin-bottom:.5rem} .ts{color:#64748b;font-size:.875rem;margin-bottom:2rem}
            .card{background:white;border:1px solid #e2e8f0;border-radius:.5rem;padding:1rem;margin-bottom:1rem}
            .card h2{margin:0 0 .5rem;font-size:1rem} table{width:100%;border-collapse:collapse}
            td{padding:.25rem .5rem;border-bottom:1px solid #f1f5f9} td:first-child{font-weight:500;width:40%}
            .overall{font-size:1.25rem;margin-bottom:1rem}
        </style></head><body>
        <h1>Diagnostico EasyStock API</h1>
        <div class="ts">{{r.Timestamp:yyyy-MM-dd HH:mm:ss}} UTC | Uptime: {{r.Uptime}} | Ambiente: {{r.Ambiente}} | Versao: {{r.Versao}}</div>
        <div class="overall">Status geral: {{Badge(r.Status)}}</div>
        {{causasHtml}}
        <div class="card"><h2>Banco de Dados</h2><table>
            <tr><td>Provider</td><td>{{r.Banco.Provider}}</td></tr>
            <tr><td>Configurado</td><td>{{r.Banco.ProviderConfigurado}}</td></tr>
            <tr><td>Fallback</td><td>{{BoolBadge(r.Banco.Fallback)}}</td></tr>
            <tr><td>Conexao</td><td>{{Badge(r.Banco.Conexao)}}</td></tr>
            <tr><td>Latencia</td><td>{{r.Banco.LatenciaMs}}ms</td></tr>
            <tr><td>Migrations</td><td>{{BoolBadge(r.Banco.MigrationsAplicadas)}}</td></tr>
        </table></div>
        <div class="card"><h2>Redis</h2><table>
            <tr><td>Configurado</td><td>{{BoolBadge(r.Redis.Configurado)}}</td></tr>
            <tr><td>Conexao</td><td>{{Badge(r.Redis.Conexao)}}</td></tr>
            <tr><td>Latencia</td><td>{{(r.Redis.Configurado ? r.Redis.LatenciaMs + "ms" : "N/A")}}</td></tr>
        </table></div>
        <div class="card"><h2>SMTP / Email</h2><table>
            <tr><td>Configurado</td><td>{{BoolBadge(r.Smtp.Configurado)}}</td></tr>
            <tr><td>Tipo</td><td>{{r.Smtp.Tipo}}</td></tr>
            <tr><td>Host</td><td>{{r.Smtp.Host ?? "N/A"}}</td></tr>
        </table></div>
        <div class="card"><h2>Storage</h2><table>
            <tr><td>Provider</td><td>{{r.Storage.Provider}}</td></tr>
            <tr><td>Diretorio existe</td><td>{{BoolBadge(r.Storage.DiretorioExiste)}}</td></tr>
        </table></div>
        <div class="card"><h2>IA (Anthropic)</h2><table>
            <tr><td>Habilitado</td><td>{{BoolBadge(r.Ia.Habilitado)}}</td></tr>
            <tr><td>API Key presente</td><td>{{BoolBadge(r.Ia.ApiKeyPresente)}}</td></tr>
        </table></div>
        <div class="card"><h2>Configuracoes</h2><table>
            <tr><td>JWT Secret presente</td><td>{{BoolBadge(r.Configuracoes.JwtSecretPresente)}}</td></tr>
            <tr><td>JWT Secret seguro</td><td>{{BoolBadge(r.Configuracoes.JwtSecretSeguro)}}</td></tr>
            <tr><td>Connection string presente</td><td>{{BoolBadge(r.Configuracoes.ConnectionStringPresente)}}</td></tr>
            <tr><td>CORS Origins</td><td>{{string.Join(", ", r.Configuracoes.CorsOrigins)}}</td></tr>
        </table></div>
        <p><a href="/diagnostico">Atualizar</a> | <a href="/swagger">Swagger</a> | <a href="/health">Health</a> | <a href="/health/ready">Readiness</a></p>
        </body></html>
        """;
    }
}

// ──────────────────────────────────────────────────────────────────────────
// DTOs
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
    public string Severidade { get; set; } = "warning"; // critical | warning | info
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
