using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EasyStock.Infra.Notifications.Hosting;

/// <summary>
/// Sinal de vida do Worker para o healthcheck do Docker (#2107). O Worker não expõe HTTP, então a cada 30 s ele regrava
/// <see cref="NotificacoesMonitoramentoOptions.ArquivoDeVida"/> enquanto os loops do motor (tag <c>dispatcher</c>) batem
/// heartbeat. Loop travado ou processo congelado deixa o arquivo envelhecer, e o Docker marca o container como unhealthy.
/// O backlog (tag <c>notificacoes</c>) fica de fora: provider stub em produção o deixa Degraded sem o Worker estar parado.
/// </summary>
public sealed class ArquivoDeVidaService(
    HealthCheckService healthChecks,
    IOptions<NotificacoesMonitoramentoOptions> options,
    ILogger<ArquivoDeVidaService> logger) : BackgroundService
{
    private static readonly TimeSpan Intervalo = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (string.IsNullOrWhiteSpace(options.Value.ArquivoDeVida))
            return;

        using var timer = new PeriodicTimer(Intervalo);
        do
        {
            await MarcarUmaVezAsync(stoppingToken);
        }
        while (await ProximoTickAsync(timer, stoppingToken));
    }

    private static async Task<bool> ProximoTickAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try { return await timer.WaitForNextTickAsync(ct); }
        catch (OperationCanceledException) { return false; }
    }

    /// <summary>Regrava o arquivo se os loops estão <c>Healthy</c>. Nunca lança: o sinal de vida não derruba o Worker.</summary>
    public async Task MarcarUmaVezAsync(CancellationToken ct)
    {
        var arquivo = options.Value.ArquivoDeVida;
        if (string.IsNullOrWhiteSpace(arquivo))
            return;

        try
        {
            var relatorio = await healthChecks.CheckHealthAsync(registro => registro.Tags.Contains("dispatcher"), ct);
            if (relatorio.Status != HealthStatus.Healthy)
            {
                logger.LogWarning("Loops do motor {Status}: arquivo de vida não renovado.", relatorio.Status);
                return;
            }

            await File.WriteAllTextAsync(arquivo, DateTimeOffset.UtcNow.ToString("O"), ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Falha ao renovar o arquivo de vida do Worker.");
        }
    }
}
