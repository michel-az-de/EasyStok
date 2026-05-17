using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace EasyStock.Infra.Notifications.Hosting;

public static class NotificationsHostingHealthCheckBuilderExtensions
{
    /// <summary>
    /// Registra <see cref="NotificationsHostingHealthCheck"/> no pipeline de health checks.
    /// Tag default <c>dispatcher</c> permite expor um endpoint dedicado
    /// (<c>/health/dispatcher</c>) separado do health da API HTTP — evita cascata
    /// onde um loop travado marca a API inteira como Unhealthy nos LBs/orquestradores.
    /// </summary>
    public static IHealthChecksBuilder AddNotificationsHosting(
        this IHealthChecksBuilder builder,
        string name = "NotificationsHosting",
        HealthStatus? failureStatus = null,
        IEnumerable<string>? tags = null)
    {
        var tagList = tags?.ToArray() ?? new[] { "dispatcher" };
        return builder.AddCheck<NotificationsHostingHealthCheck>(name, failureStatus, tagList);
    }
}
