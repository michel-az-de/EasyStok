using System.Diagnostics;
using EasyStock.Api.BackgroundServices;
using EasyStock.Api.Configuration;
using EasyStock.Api.Observability;
using EasyStock.Infra.Postgre.Data;
using Microsoft.Extensions.Caching.Distributed;

namespace EasyStock.Api.Controllers;

/// <summary>
/// Endpoints de infraestrutura, operações, SLO, alertas e métricas avançadas.
/// Separado de <see cref="DiagnosticoController"/> por responsabilidade.
/// </summary>
[ApiController]
[Route("api/diagnostico")]
[Route("diagnostico")]
[Authorize(Policy = "Admin")]
[ApiExplorerSettings(GroupName = "v1-ptbr")]
public sealed class DiagnosticoInfraController(
    ResolvedInfrastructureState infraState,
    IConfiguration configuration,
    IDistributedCache cache,
    HealthSnapshotService healthSnapshotService,
    IHttpClientFactory httpClientFactory,
    ILogger<DiagnosticoInfraController> logger) : ControllerBase
{
    [HttpGet("endpoints")]
    public async Task<IActionResult> TestEndpoints(CancellationToken ct)
    {
        // Cache de 60s — o próprio teste consome ~4s; rodar a cada request degradaria a API.
        // v2: respostas agora incluem campo `degradados` + status "degraded" para auth-protegidos.
        const string cacheKey = "diag:endpoints:v2";
        try
        {
            var cached = await cache.GetStringAsync(cacheKey, ct);
            if (cached is not null)
            {
                var cachedObj = System.Text.Json.JsonSerializer.Deserialize<object>(cached);
                return Ok(cachedObj);
            }
        }
        catch { /* cache indisponível — continua sem cache */ }

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
                var httpResp = await client.GetAsync(baseUrl + route, ct);
                sw.Stop();

                result.StatusCode = (int)httpResp.StatusCode;
                result.LatenciaMs = sw.ElapsedMilliseconds;

                // Endpoint com expectedStatus=401 só testa que a auth está protegida — o teste
                // não envia credenciais, então não conseguimos afirmar saúde funcional. Marcamos
                // como "degraded" (amarelo, auth-protegido) em vez de "ok" para não dar falsa
                // impressão de que o endpoint serve dados corretamente.
                if (expectedStatus == 401 && result.StatusCode == 401)
                {
                    result.Status = "degraded";
                    result.Erro = "Auth-protegido — teste sem credenciais, comportamento funcional não verificado.";
                }
                else if (result.StatusCode == expectedStatus && result.StatusCode < 400)
                {
                    result.Status = result.LatenciaMs < 300 ? "ok"
                        : result.LatenciaMs < 1000 ? "slow"
                        : "very_slow";
                }
                else
                {
                    result.Status = "error";
                }
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
        var degraded = results.Count(r => r.Status == "degraded");
        var failed = results.Count(r => r.Status is "error" or "timeout");

        var response = new EndpointsTestResponse
        {
            Resultados = results.ToArray(),
            Saudaveis = healthy,
            Lentos = slow,
            Degradados = degraded,
            Falhas = failed,
            TestadoEm = DateTimeOffset.UtcNow
        };

        try
        {
            var json = System.Text.Json.JsonSerializer.Serialize(response);
            await cache.SetStringAsync(cacheKey, json,
                new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(60)
                }, ct);
        }
        catch { /* falha silenciosa no cache */ }

        return Ok(response);
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

    /// <summary>
    /// Zera o histórico de health snapshots (gráficos e contadores do dashboard).
    /// Use após um deploy ou correção de problemas para obter uma linha de base limpa.
    /// </summary>
    [HttpPost("historico/zerar")]
    public async Task<IActionResult> ZerarHistorico(CancellationToken ct)
    {
        await healthSnapshotService.ZerarHistoricoAsync(ct);
        logger.LogInformation("Histórico de health snapshots zerado via endpoint.");
        return Ok(new { success = true, mensagem = "Histórico zerado. Novos snapshots começarão a ser coletados em até 60 segundos.", zeradoEm = DateTimeOffset.UtcNow });
    }

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

        var snapshots = healthSnapshotService.GetSnapshots();
        var periodSnaps = snapshots.Where(s => s.Timestamp >= cutoff).ToList();
        double? uptime = null;
        if (periodSnaps.Count > 0)
            uptime = Math.Round(periodSnaps.Count(s => s.OverallStatus != "critical") * 100.0 / periodSnaps.Count, 2);

        var logsDir = GetLogDirectory();
        double? avg = null, p95 = null;
        int totalRequests = 0, totalErrors = 0;

        try
        {
            var entries = DiagnosticoLogAnalyzer.ParseAllLogFiles(logsDir, cutoff);
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
        if (!infraState.DatabaseProvider.Equals("postgresql", StringComparison.OrdinalIgnoreCase))
            return Ok(new { disponivel = false, motivo = "Apenas disponível com PostgreSQL." });

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
    // Helpers privados
    // ──────────────────────────────────────────────────────────────────────

    private string GetLogDirectory() =>
        configuration[ConfigurationKeys.LogDirectory] is { Length: > 0 } configured
            ? configured
            : Path.Combine(AppContext.BaseDirectory, "logs");
}
