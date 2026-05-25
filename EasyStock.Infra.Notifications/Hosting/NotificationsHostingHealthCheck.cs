using EasyStock.Application.Services.Notifications;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace EasyStock.Infra.Notifications.Hosting;

/// <summary>
/// Verifica se os 3 BackgroundServices do pipeline (Dispatcher, Avaliador,
/// Coletor) bateram heartbeat dentro de uma janela proporcional ao intervalo
/// configurado de cada loop. Disabled quando <see cref="NotificationsHostingOptions.Mode"/>
/// = <see cref="NotificationsHostingMode.Disabled"/> — retorna Healthy com motivo.
/// Janela = 5x intervalo configurado, com floor de 60s pro dispatcher pra acomodar
/// wakeup por signaler.
/// </summary>
public sealed class NotificationsHostingHealthCheck(
    IOptions<NotificationsHostingOptions> options,
    INotificationsLoopHeartbeat heartbeat,
    TimeProvider time) : IHealthCheck
{
    private static readonly TimeSpan DispatcherWindowFloor = TimeSpan.FromSeconds(60);

    public NotificationsHostingHealthCheck(
        IOptions<NotificationsHostingOptions> options,
        INotificationsLoopHeartbeat heartbeat)
        : this(options, heartbeat, TimeProvider.System) { }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var opts = options.Value;
        if (opts.Mode != NotificationsHostingMode.Hosted)
            return Task.FromResult(HealthCheckResult.Healthy(
                "Notifications hosting disabled neste host (pipeline roda em outro processo)."));

        var now = time.GetUtcNow();
        var data = new Dictionary<string, object>(StringComparer.Ordinal);
        var problems = new List<string>();

        var dispatcherWindow = TimeSpan.FromMilliseconds(opts.DispatcherPollingIntervalMs * 5);
        if (dispatcherWindow < DispatcherWindowFloor) dispatcherWindow = DispatcherWindowFloor;

        Verificar(NotificationsLoops.Dispatcher, dispatcherWindow);
        Verificar(NotificationsLoops.Avaliador, TimeSpan.FromSeconds(opts.AvaliadorIntervalSeconds * 5));
        Verificar(NotificationsLoops.Coletor, TimeSpan.FromSeconds(opts.ColetorIntervalSeconds * 5));

        return Task.FromResult(problems.Count == 0
            ? HealthCheckResult.Healthy("Loops batendo dentro da janela.", data)
            : HealthCheckResult.Unhealthy(string.Join("; ", problems), data: data));

        void Verificar(string name, TimeSpan janela)
        {
            data[$"{name}_window_seconds"] = (int)janela.TotalSeconds;
            var last = heartbeat.LastBeat(name);
            if (last is null)
            {
                data[$"{name}_last_beat"] = "never";
                problems.Add($"{name}: sem heartbeat desde startup");
                return;
            }

            data[$"{name}_last_beat"] = last.Value.ToString("o");
            var idle = now - last.Value;
            data[$"{name}_idle_seconds"] = (int)idle.TotalSeconds;
            if (idle > janela)
                problems.Add(
                    $"{name}: idle {(int)idle.TotalSeconds}s > janela {(int)janela.TotalSeconds}s");
        }
    }
}
