using System.Diagnostics.Metrics;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Application.Services.Notifications;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums.Notifications;
using EasyStock.Infra.Postgre.Concurrency;
using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EasyStock.Worker.BackgroundServices;

/// <summary>
/// Monitor de saude de endpoints criticos. A cada tick faz GET em endpoints publicos (anonimos) do API e rastreia falhas
/// consecutivas. A maquina de estado e do <see cref="AvaliadorIncidente"/> (N10): abre na N-esima falha seguida, re-emite
/// enquanto aberto e resolve depois de 2 verificacoes boas. O aviso sai por <see cref="IPublicadorIncidenteSistema"/>
/// (superadmins por e-mail e WhatsApp), nunca por HTTP. O estado em <c>endpoint_health_state</c> sobrevive a restart.
///
/// Padrao espelha SlaMonitorService: advisory lock pra single-instance, scope por tick, falhas isoladas nao quebram
/// outras checagens. <c>LastFailureMessage</c> guarda so um codigo fechado (HTTP_503, TIMEOUT, CONEXAO_RECUSADA, OUTRO).
///
/// Config (appsettings ou env):
///   EndpointHealth:BaseUrl              base URL do API (ex: https://api.exemplo.com)
///   EndpointHealth:FailureThreshold     N falhas consecutivas pra abrir o incidente (default 3)
///   Notifications:Incidentes:*          interruptor e janela de dedupe (ver PublicadorIncidenteSistema)
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

            var threshold = Math.Max(1, configuration.GetValue<int>("EndpointHealth:FailureThreshold", 3));
            var limiares = LimiaresIncidente.Endpoint with { FalhasParaAbrir = threshold };

            var http = httpClientFactory.CreateClient("endpoint-health");
            http.BaseAddress = new Uri(baseUrl);
            http.Timeout = TimeSpan.FromSeconds(10);

            foreach (var (name, path) in Endpoints)
            {
                await CheckEndpointAsync(name, path, http, db, limiares, ct);
            }
        }, ct);
    }

    private async Task CheckEndpointAsync(
        string name, string path, HttpClient http, EasyStockDbContext db,
        LimiaresIncidente limiares, CancellationToken ct)
    {
        var state = await db.EndpointHealthStates
            .FirstOrDefaultAsync(s => s.EndpointName == name, ct);
        if (state == null)
        {
            state = new EndpointHealthState { EndpointName = name };
            db.EndpointHealthStates.Add(state);
        }

        var agora = DateTime.UtcNow;
        CheckCounter.Add(1, new KeyValuePair<string, object?>("endpoint", name));

        bool healthy;
        string? codigoFalha = null;
        try
        {
            using var resp = await http.GetAsync(path, ct);
            healthy = resp.IsSuccessStatusCode;
            if (!healthy)
                codigoFalha = CodigoFalhaIncidente.DeStatusHttp((int)resp.StatusCode);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            healthy = false;
            codigoFalha = CodigoFalhaIncidente.DeExcecao(ex);
        }

        var aberto = state.LastAlertedAt is not null;
        var avaliacao = AvaliadorIncidente.Avaliar(state, healthy, agora, limiares);
        if (!healthy) state.LastFailureMessage = codigoFalha;

        switch (avaliacao.Decisao)
        {
            case DecisaoIncidente.Abrir:
            case DecisaoIncidente.Reavisar:
                if (avaliacao.Decisao == DecisaoIncidente.Abrir)
                    AlertCounter.Add(1, new KeyValuePair<string, object?>("endpoint", name));
                logger.LogWarning("Endpoint {Name} com problema ({Decisao}, {Falhas} falhas seguidas)",
                    name, avaliacao.Decisao, state.ConsecutiveFailures);
                await PublicarIncidenteAsync(EstadoIncidente.ComProblema,
                    SeveridadeIncidente.Alta, avaliacao.DesdeUtc ?? agora, ct);
                break;
            case DecisaoIncidente.Resolver:
                logger.LogInformation("Endpoint {Name} normalizado", name);
                await PublicarIncidenteAsync(EstadoIncidente.Normalizado,
                    SeveridadeIncidente.Media, avaliacao.DesdeUtc ?? agora, ct);
                break;
            default:
                if (healthy && aberto)
                    logger.LogInformation("Endpoint {Name} respondeu; aguardando nova verificacao boa para normalizar", name);
                break;
        }

        // O estado so e gravado depois do aviso: se a publicacao falhar, a excecao aborta o tick sem gravar e a
        // proxima verificacao decide de novo (a chave de dedupe do outbox segura o aviso em dobro).
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Publica num escopo de DI novo. A conexao do escopo do tick foi aberta pelo advisory lock antes de haver tenant;
    /// publicar nela fazia o INSERT em <c>notif_eventos</c> (RLS FORCE) violar o WITH CHECK (42501). O escopo novo abre a
    /// propria conexao ja com o tenant da empresa padrao (mesmo padrao do HealthSnapshotService e do N1 do
    /// LembretesPedidoAgendadoTick).
    /// </summary>
    private async Task PublicarIncidenteAsync(
        EstadoIncidente estado, SeveridadeIncidente severidade, DateTime desdeUtc, CancellationToken ct)
    {
        using var scope = serviceProvider.CreateScope();
        var publicador = scope.ServiceProvider.GetRequiredService<IPublicadorIncidenteSistema>();
        await publicador.PublicarAsync(ComponenteIncidente.Api, estado, severidade, desdeUtc, ct);
    }
}
