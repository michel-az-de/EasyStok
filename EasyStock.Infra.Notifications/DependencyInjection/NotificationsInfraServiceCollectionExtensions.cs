using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Infra.Notifications.Templating;
using Microsoft.Extensions.DependencyInjection;

namespace EasyStock.Infra.Notifications.DependencyInjection;

public static class NotificationsInfraServiceCollectionExtensions
{
    /// <summary>
    /// Registra os componentes de infraestrutura do módulo de notificações.
    /// Os adapters de canal (Email, SMS, WhatsApp, InApp) são registrados no PR3.
    /// </summary>
    public static IServiceCollection AddNotificationsInfra(this IServiceCollection services)
    {
        services.AddScoped<IRendererTemplate, ScribanRenderer>();
        return services;
    }
}
