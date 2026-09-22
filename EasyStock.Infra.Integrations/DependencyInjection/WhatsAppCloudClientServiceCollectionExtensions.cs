using System.Net.Http.Headers;
using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Infra.Integrations.WhatsApp;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace EasyStock.Infra.Integrations.DependencyInjection;

/// <summary>
/// Registra <see cref="IWhatsAppCloudClient"/>: <see cref="WhatsAppCloudClient"/> real (HttpClient
/// nomeado "whatsapp-cloud", bearer + base address da Meta) quando
/// <c>Notifications:WhatsApp:Provider=meta</c>, senão <see cref="StubWhatsAppCloudClient"/> — mesmo
/// switch já usado para <c>IProvedorWhatsApp</c> em NotificationsInfraServiceCollectionExtensions.
/// </summary>
public static class WhatsAppCloudClientServiceCollectionExtensions
{
    public static IServiceCollection AddEasyStockWhatsAppCloudClient(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<WhatsAppCloudOptions>(configuration.GetSection("Notifications:WhatsApp:Meta"));

        services.AddHttpClient<WhatsAppCloudClient>("whatsapp-cloud", (sp, client) =>
        {
            var opts = sp.GetRequiredService<IOptions<WhatsAppCloudOptions>>().Value;
            if (!string.IsNullOrWhiteSpace(opts.BaseUrl))
                client.BaseAddress = new Uri(opts.BaseUrl.TrimEnd('/') + "/");
            if (!string.IsNullOrWhiteSpace(opts.AccessToken))
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", opts.AccessToken);
        });

        services.AddScoped<StubWhatsAppCloudClient>();

        var provider = configuration["Notifications:WhatsApp:Provider"] ?? "stub";
        if (string.Equals(provider, "meta", StringComparison.OrdinalIgnoreCase))
            services.AddScoped<IWhatsAppCloudClient>(sp => sp.GetRequiredService<WhatsAppCloudClient>());
        else
            services.AddScoped<IWhatsAppCloudClient>(sp => sp.GetRequiredService<StubWhatsAppCloudClient>());

        return services;
    }
}
