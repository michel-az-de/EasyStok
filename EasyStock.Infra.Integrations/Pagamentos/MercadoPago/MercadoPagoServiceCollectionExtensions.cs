using EasyStock.Application.Ports.Output.Pagamentos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EasyStock.Infra.Integrations.Pagamentos.MercadoPago;

public static class MercadoPagoServiceCollectionExtensions
{
    /// <summary>
    /// Registra <see cref="IMercadoPagoClient"/>. Usa <see cref="StubMercadoPagoClient"/>
    /// quando <c>MercadoPago:UseStub=true</c> (padrão em Development).
    /// </summary>
    public static IServiceCollection AddMercadoPagoClient(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Options sempre registrado — WebhookSecret é usado tanto por adapter real quanto stub.
        services.Configure<MercadoPagoOptions>(configuration.GetSection(MercadoPagoOptions.Section));

        // Webhook secret resolver — ADR-0006 (MVP single-tenant via config).
        services.AddScoped<IMpWebhookSecretProvider, MercadoPagoWebhookSecretProvider>();

        var useStub = configuration.GetValue<bool>("MercadoPago:UseStub");

        if (useStub)
        {
            services.AddScoped<IMercadoPagoClient, StubMercadoPagoClient>();
        }
        else
        {
            services.AddHttpClient<MercadoPagoClient>(client =>
            {
                var baseUrl = configuration["MercadoPago:BaseUrl"] ?? "https://api.mercadopago.com/";
                client.BaseAddress = new Uri(baseUrl);
                client.Timeout = TimeSpan.FromSeconds(10); // timeout externo de segurança
            });
            services.AddScoped<IMercadoPagoClient, MercadoPagoClient>();
        }

        return services;
    }
}
