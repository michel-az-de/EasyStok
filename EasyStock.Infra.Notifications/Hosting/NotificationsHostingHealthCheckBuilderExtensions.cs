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

    /// <summary>
    /// Registra <see cref="NotificacoesBacklogHealthCheck"/> com a tag <c>notificacoes</c> (N1): o endpoint
    /// <c>/health/notificacoes</c> e o ping do Worker o selecionam por ela, e ele fica de fora de <c>/health</c> e
    /// <c>/health/ready</c> para um backlog ruim não tirar a API do balanceador.
    /// </summary>
    public static IHealthChecksBuilder AddNotificacoesBacklog(
        this IHealthChecksBuilder builder,
        string name = "NotificacoesBacklog",
        HealthStatus? failureStatus = null)
        => builder.AddCheck<NotificacoesBacklogHealthCheck>(name, failureStatus, [NotificacoesBacklogHealthCheck.Tag]);
}
