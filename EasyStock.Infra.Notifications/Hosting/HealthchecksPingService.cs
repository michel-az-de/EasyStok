using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EasyStock.Infra.Notifications.Hosting;

/// <summary>
/// Monitoramento externo do motor (seção <c>Notifications:Monitoring</c>).
/// </summary>
public sealed class NotificacoesMonitoramentoOptions
{
    public const string Section = "Notifications:Monitoring";

    /// <summary>
    /// URL de ping do Healthchecks.io (no <c>.env</c>, nunca no repositório). Vazia desliga o serviço: sem ela o Worker não
    /// pinga e ninguém avisa que ele parou.
    /// </summary>
    public string? PingUrl { get; set; }

    /// <summary>Intervalo entre pings, em segundos. Padrão 60: no máximo um ping por minuto, não um por rodada de 10 s.</summary>
    public int IntervaloSegundos { get; set; } = 60;
}

/// <summary>
/// Pinga o Healthchecks.io a cada minuto (N1, ver e avisar) enquanto os checks do motor (heartbeat dos loops e backlog)
/// estão saudáveis, e chama <c>/fail</c> quando não. O Worker não tem endpoint HTTP, então é o ping que conta ao mundo que
/// ele está vivo: sem ping o serviço alerta pela ausência; com <c>/fail</c>, pela causa.
/// </summary>
public sealed class HealthchecksPingService(
    HealthCheckService healthChecks,
    IHttpClientFactory httpClientFactory,
    IOptions<NotificacoesMonitoramentoOptions> options,
    ILogger<HealthchecksPingService> logger) : BackgroundService
{
    /// <summary>Nome do <see cref="HttpClient"/> nomeado que o host registra para o ping.</summary>
    public const string NomeDoCliente = "healthchecks";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (string.IsNullOrWhiteSpace(options.Value.PingUrl))
        {
            logger.LogInformation("HealthchecksPingService desligado: Notifications:Monitoring:PingUrl vazia.");
            return;
        }

        var intervalo = TimeSpan.FromSeconds(Math.Max(10, options.Value.IntervaloSegundos));
        using var timer = new PeriodicTimer(intervalo);
        do
        {
            await PingarUmaVezAsync(stoppingToken);
        }
        while (await ProximoTickAsync(timer, stoppingToken));
    }

    private static async Task<bool> ProximoTickAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try { return await timer.WaitForNextTickAsync(ct); }
        catch (OperationCanceledException) { return false; }
    }

    /// <summary>
    /// Avalia os checks do motor (tags <c>notificacoes</c> e <c>dispatcher</c>) e pinga a URL base, ou <c>/fail</c> se
    /// algum não está <c>Healthy</c>. Nunca lança: o monitor não pode derrubar o Worker.
    /// </summary>
    public async Task PingarUmaVezAsync(CancellationToken ct)
    {
        var url = options.Value.PingUrl;
        if (string.IsNullOrWhiteSpace(url)) return;

        try
        {
            var relatorio = await healthChecks.CheckHealthAsync(
                registro => registro.Tags.Contains(NotificacoesBacklogHealthCheck.Tag) || registro.Tags.Contains("dispatcher"), ct);

            var destino = relatorio.Status == HealthStatus.Healthy ? url : url.TrimEnd('/') + "/fail";
            if (relatorio.Status != HealthStatus.Healthy)
                logger.LogWarning("Motor de notificações {Status}: ping /fail ao Healthchecks.io.", relatorio.Status);

            using var cliente = httpClientFactory.CreateClient(NomeDoCliente);
            cliente.Timeout = TimeSpan.FromSeconds(10);
            using var resposta = await cliente.GetAsync(destino, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Falha ao pingar o Healthchecks.io; tenta no próximo minuto.");
        }
    }
}
