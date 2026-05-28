using System.Diagnostics;
using System.Reflection;
using EasyStock.Api.BackgroundServices;
using EasyStock.Api.Configuration;
using EasyStock.Api.Observability;
using EasyStock.Infra.Postgre.Data;
using EasyStock.Application.Ports.Output;
using Azure.Storage.Files.Shares;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;

namespace EasyStock.Api.Controllers;

[ApiController]
[Route("api/diagnostico")]
[Route("diagnostico")]
[Authorize(Policy = "Admin")]
[ApiExplorerSettings(GroupName = "v1-ptbr")]
public sealed class DiagnosticoController(
    ResolvedInfrastructureState infraState,
    IConfiguration configuration,
    IDistributedCache cache,
    IEmailService emailService,
    HealthSnapshotService healthSnapshotService,
    ILogger<DiagnosticoController> logger) : ControllerBase
{
    [HttpGet("ping")]
    [AllowAnonymous]
    public IActionResult Ping() => Ok(new { pong = true, timestamp = DateTimeOffset.UtcNow });

    [HttpGet]
    public async Task<IActionResult> Diagnostico(CancellationToken ct)
    {
        // Executar checks de infra em paralelo para reduzir latência.
        // Usar await direto nas tasks iniciadas garante que qualquer exceção
        // seja propagada sem bloquear thread via .Result.
        var bancoTask   = GetBancoStatusAsync(ct);
        var redisTask   = GetRedisStatusAsync(ct);
        var storageTask = GetStorageStatusAsync(ct);

        var banco   = await bancoTask;
        var redis   = await redisTask;
        var storage = await storageTask;

        var result = new DiagnosticoResult
        {
            Status        = "ok",
            Timestamp     = DateTimeOffset.UtcNow,
            Ambiente      = infraState.Environment,
            Uptime        = FormatUptime(DateTimeOffset.UtcNow - infraState.StartupTime),
            Versao        = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0",
            Banco         = banco,
            Redis         = redis,
            Smtp          = GetSmtpStatus(),
            Storage       = storage,
            Ia            = GetIaStatus(),
            Configuracoes = GetConfiguracoesStatus()
        };

        // Determinar status geral — hierarquia: critical > degraded > ok.
        // Considera todos os subsistemas relevantes; status verde só com tudo saudável.
        const long LatenciaBancoDegradedMs = 1000;

        var isCritical =
            result.Banco.Conexao == "falha" ||
            !result.Configuracoes.JwtSecretPresente ||
            !result.Configuracoes.ConnectionStringPresente;

        var isDegraded =
            result.Redis.Conexao == "falha" ||
            result.Banco.Fallback ||
            // JwtSecretSeguro é nullable — só conta como degradação quando explicitamente inseguro.
            result.Configuracoes.JwtSecretSeguro == false ||
            // IA habilitada mas sem API Key configurada — recurso degradado.
            (result.Ia.Habilitado && !result.Ia.ApiKeyPresente) ||
            // Storage configurado mas diretório inacessível (false != null: null = não verificado).
            (result.Storage.Configurado && result.Storage.DiretorioExiste == false) ||
            // Latência de banco acima do threshold de boot/cold start sinaliza degradação real.
            result.Banco.LatenciaMs > LatenciaBancoDegradedMs;

        if (isCritical)
            result.Status = "critical";
        else if (isDegraded)
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
                            // Evita realocacao de lista inteira ao truncar — modifica a lista existente
                            // removendo entries do inicio, que e ~O(n) mas sem alocar uma 2a list grande.
                            if (allEntries.Count > 5000)
                            {
                                allEntries.RemoveRange(0, allEntries.Count - 5000);
                                break;
                            }
                        }

                        enhancedLogs = new EnhancedLogsResult
                        {
                            Disponivel = true,
                            QueryTimestamp = DateTimeOffset.UtcNow,
                            PeriodoHoras = 48,
                            TotalEntries = allEntries.Count,
                            Entradas = allEntries.TakeLast(500).ToArray(),
                            Resumo = DiagnosticoLogAnalyzer.BuildLogSummary(allEntries),
                            Padroes = DiagnosticoLogAnalyzer.DetectPatterns(allEntries).ToArray()
                        };
                    }
                }
            }
            catch (Exception ex) { logger.LogDebug(ex, "Log parsing failed — dashboard will render without log data."); }

            return Content(DiagnosticoHtmlRenderer.Render(result, snapshots, enhancedLogs, GetLogDirectory()), "text/html; charset=utf-8");
        }

        return Ok(result);
    }

    [HttpGet("banco")]
    public async Task<IActionResult> TesteBanco(CancellationToken ct)
    {
        var status = await GetBancoStatusAsync(ct);
        return Ok(status);
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
            if (infraState.DatabaseProvider is "postgresql")
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

    private string GetLogDirectory() =>
        configuration[ConfigurationKeys.LogDirectory] is { Length: > 0 } configured
            ? configured
            : Path.Combine(AppContext.BaseDirectory, "logs");

}
