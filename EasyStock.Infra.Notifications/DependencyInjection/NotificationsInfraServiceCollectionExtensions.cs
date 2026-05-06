using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Infra.Notifications.Email;
using EasyStock.Infra.Notifications.InApp;
using EasyStock.Infra.Notifications.Options;
using EasyStock.Infra.Notifications.Sms;
using EasyStock.Infra.Notifications.Templating;
using EasyStock.Infra.Notifications.WhatsApp;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EasyStock.Infra.Notifications.DependencyInjection;

public static class NotificationsInfraServiceCollectionExtensions
{
    /// <summary>
    /// Registra todos os componentes de infraestrutura do módulo de notificações.
    /// O provider ativo por canal é determinado por configuração (Notifications:Sms:Provider, etc.).
    /// </summary>
    public static IServiceCollection AddNotificationsInfra(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Renderer
        services.AddScoped<IRendererTemplate, ScribanRenderer>();

        // Canais
        services.AddScoped<ICanalNotificacao, SmtpEmailCanal>();
        services.AddScoped<ICanalNotificacao, SmsCanal>();
        services.AddScoped<ICanalNotificacao, WhatsAppCanal>();
        services.AddScoped<ICanalNotificacao, InAppCanal>();

        // SMS providers (todos registrados, selecionado por chave "sms:active")
        services.Configure<TwilioSmsOptions>(configuration.GetSection("Notifications:Sms:Twilio"));
        services.Configure<ZenviaSmsOptions>(configuration.GetSection("Notifications:Sms:Zenvia"));

        services.AddKeyedScoped<IProvedorSms, StubSmsProvider>("sms:stub");
        services.AddKeyedScoped<IProvedorSms, TwilioSmsProvider>("sms:twilio");
        services.AddKeyedScoped<IProvedorSms, ZenviaSmsProvider>("sms:zenvia");

        var smsProvider = configuration["Notifications:Sms:Provider"] ?? "stub";
        services.AddKeyedScoped<IProvedorSms>(
            "sms:active",
            (sp, _) => sp.GetRequiredKeyedService<IProvedorSms>($"sms:{smsProvider}"));

        // WhatsApp providers
        services.Configure<TwilioWhatsAppOptions>(configuration.GetSection("Notifications:WhatsApp:Twilio"));
        services.Configure<MetaCloudWhatsAppOptions>(configuration.GetSection("Notifications:WhatsApp:Meta"));

        services.AddKeyedScoped<IProvedorWhatsApp, StubWhatsAppProvider>("whatsapp:stub");
        services.AddKeyedScoped<IProvedorWhatsApp, TwilioWhatsAppProvider>("whatsapp:twilio");
        services.AddKeyedScoped<IProvedorWhatsApp, MetaCloudWhatsAppProvider>("whatsapp:meta");

        var waProvider = configuration["Notifications:WhatsApp:Provider"] ?? "stub";
        services.AddKeyedScoped<IProvedorWhatsApp>(
            "whatsapp:active",
            (sp, _) => sp.GetRequiredKeyedService<IProvedorWhatsApp>($"whatsapp:{waProvider}"));

        // Named HttpClients para providers externos
        services.AddHttpClient("TwilioSms");
        services.AddHttpClient("ZenviaSms");
        services.AddHttpClient("TwilioWhatsApp");
        services.AddHttpClient("MetaWhatsApp");

        return services;
    }
}
