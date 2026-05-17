using System.Diagnostics.Metrics;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using EasyStock.Domain.Entities;
using EasyStock.Infra.Postgre.Concurrency;
using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EasyStock.Worker.BackgroundServices;

/// <summary>
/// Monitor de saude de endpoints criticos. A cada tick faz GET em endpoints
/// publicos (anonimos) do API e rastreia falhas consecutivas. Quando bate
/// threshold + nao alertou nas ultimas 24h, abre um ticket BugFixDev via
/// POST /api/ci/tickets (mesmo endpoint que CI e smoke usam — dogfooding).
///
/// Padrao espelha SlaMonitorService: advisory lock pra single-instance, scope
/// por tick, falhas isoladas nao quebram outras checagens. Estado persistido
/// em endpoint_health_state pra idempotencia atravessar restarts do worker.
///
/// Config (appsettings ou env):
///   EndpointHealth:BaseUrl              base URL do API (ex: https://api.exemplo.com)
///   EndpointHealth:FailureThreshold     N falhas consecutivas pra alertar (default 3)
///   EndpointHealth:CooldownHours        horas entre alertas do mesmo endpoint (default 24)
///   Ci:AutoTicketKey                    chave compartilhada pra chamar /api/ci/tickets
/// </summary>
public sealed class EndpointHealthMonitorService(
    IServiceProvider serviceProvider,
    IOptions<WorkerOptions> options,
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    ILogger<EndpointHealthMonitorService> logger) : BackgroundService
{
    // PEHM = Plataforma Endpoint Health Monitor
    private const long LockId = 0x5045_484D_0000_0001L;

    private static readonly Meter Meter = new("EasyStock.EndpointHealth", "1.0");
    private static readonly Counter<long> CheckCounter = Meter.CreateCounter<long>("endpoint_health.checks", "checks");
    private static readonly Counter<long> AlertCounter = Meter.CreateCounter<long>("endpoint_health.alerts", "alerts");

    // Endpoints monitorados. Apenas anonimos — autenticados precisariam de
    // pareamento sintetico, complexidade adicional. /version ja exercita
    // HTTP + DI + DB num unico endpoint barato.
    private static readonly (string Name, string Path)[] Endpoints =
    {
        ("api/mobile/version", "/api/mobile/version")
    };

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("EndpointHealthMonitorService iniciado");

        var intervalSeconds = Math.Max(60, options.Value.EndpointHealthIntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await TickAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Erro durante tick do EndpointHealthMonitorService");
            }

            try { await Task.Delay(TimeSpan.FromSeconds(intervalSeconds), stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task TickAsync(CancellationToken ct)
    {
        using var scope = serviceProvider.CreateScope();
        var sp = scope.ServiceProvider;
        var advisoryLock = sp.GetRequiredService<PostgresAdvisoryLock>();

        await advisoryLock.TentarExecutarAsync(LockId, async token =>
        {
            ct = token;
            var db = sp.GetRequiredService<EasyStockDbContext>();

            var baseUrl = configuration["EndpointHealth:BaseUrl"];
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                logger.LogDebug("EndpointHealth:BaseUrl vazio — monitor pulando");
                return;
            }

            var threshold = configuration.GetValue<int>("EndpointHealth:FailureThreshold", 3);
            var cooldownHours = configuration.GetValue<int>("EndpointHealth:CooldownHours", 24);
            var ciKey = configuration["Ci:AutoTicketKey"];

            var http = httpClientFactory.CreateClient("endpoint-health");
            http.BaseAddress = new Uri(baseUrl);
            http.Timeout = TimeSpan.FromSeconds(10);

            foreach (var (name, path) in Endpoints)
            {
                await CheckEndpointAsync(name, path, http, db,
                    threshold, cooldownHours, ciKey, baseUrl, ct);
            }
        }, ct);
    }

    private async Task CheckEndpointAsync(
        string name, string path, HttpClient http, EasyStockDbContext db,
        int threshold, int cooldownHours, string? ciKey, string baseUrl,
        CancellationToken ct)
    {
        var state = await db.EndpointHealthStates
            .FirstOrDefaultAsync(s => s.EndpointName == name, ct);
        if (state == null)
        {
            state = new EndpointHealthState { EndpointName = name };
            db.EndpointHealthStates.Add(state);
        }

        var agora = DateTime.UtcNow;
        state.LastCheckAt = agora;
        CheckCounter.Add(1, new KeyValuePair<string, object?>("endpoint", name));

        bool healthy;
        string? failureMessage = null;
        try
        {
            using var resp = await http.GetAsync(path, ct);
            healthy = resp.IsSuccessStatusCode;
            if (!healthy)
                failureMessage = $"HTTP {(int)resp.StatusCode}";
        }
        catch (Exception ex)
        {
            healthy = false;
            failureMessage = ex.GetType().Name + ": " + Truncate(ex.Message, 256);
        }

        if (healthy)
        {
            if (state.ConsecutiveFailures > 0)
                logger.LogInformation("Endpoint {Name} se recuperou apos {Falhas} falhas",
                    name, state.ConsecutiveFailures);
            state.ConsecutiveFailures = 0;
            state.AtualizadoEm = agora;
            await db.SaveChangesAsync(ct);
            return;
        }

        state.ConsecutiveFailures++;
        state.LastFailureAt = agora;
        state.LastFailureMessage = failureMessage;
        state.AtualizadoEm = agora;

        var cooldownExpired = state.LastAlertedAt is null
            || (agora - state.LastAlertedAt.Value) >= TimeSpan.FromHours(cooldownHours);

        if (state.ConsecutiveFailures >= threshold && cooldownExpired && !string.IsNullOrWhiteSpace(ciKey))
        {
            var ticketId = await TryOpenTicketAsync(http, ciKey, name, state, threshold, ct);
            if (ticketId.HasValue)
            {
                state.LastAlertedAt = agora;
                state.LastAlertedTicketId = ticketId;
                AlertCounter.Add(1, new KeyValuePair<string, object?>("endpoint", name));
                logger.LogWarning("Ticket runtime aberto pro endpoint {Name}: {TicketId}", name, ticketId);
            }
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Chama POST /api/ci/tickets via HTTP. Reusa o endpoint que CI e smoke
    /// tambem usam — dogfood. Signature deterministico por dia evita criar
    /// 100 tickets do mesmo endpoint quebrado.
    /// </summary>
    private async Task<Guid?> TryOpenTicketAsync(
        HttpClient http, string ciKey, string endpointName,
        EndpointHealthState state, int threshold, CancellationToken ct)
    {
        try
        {
            var signature = Sha256Hex($"runtime|{endpointName}|{DateTime.UtcNow:yyyy-MM-dd}");
            var payload = new
            {
                origin = "runtime",
                signature,
                titulo = $"Endpoint {endpointName} degradado",
                descricao = $"Falhou {state.ConsecutiveFailures}x consecutivas (threshold {threshold}). " +
                            $"Ultima mensagem: {state.LastFailureMessage ?? "(sem detalhe)"}",
                contexto = $"endpoint={endpointName}\nlastFailureAt={state.LastFailureAt:O}"
            };

            using var req = new HttpRequestMessage(HttpMethod.Post, "/api/ci/tickets")
            {
                Content = JsonContent.Create(payload)
            };
            req.Headers.Add("X-Ci-Key", ciKey);

            using var resp = await http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode)
            {
                logger.LogWarning("Falha ao POST /api/ci/tickets: {Status}", (int)resp.StatusCode);
                return null;
            }
            var body = await resp.Content.ReadFromJsonAsync<AutoTicketResponse>(cancellationToken: ct);
            return body?.TicketId;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erro chamando /api/ci/tickets pro endpoint {Name}", endpointName);
            return null;
        }
    }

    private static string Sha256Hex(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max];

    private sealed record AutoTicketResponse(Guid TicketId, bool Created);
}
