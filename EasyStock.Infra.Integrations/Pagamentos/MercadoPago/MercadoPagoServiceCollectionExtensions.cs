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
        var useStub = configuration.GetValue<bool>("MercadoPago:UseStub");

        if (useStub)
        {
            services.AddScoped<IMercadoPagoClient, StubMercadoPagoClient>();
        }
        else
        {
            services.Configure<MercadoPagoOptions>(configuration.GetSection(MercadoPagoOptions.Section));
            // S32: registra a PORTA como client tipado. O antigo AddHttpClient<MercadoPagoClient> +
            // AddScoped<IMercadoPagoClient, MercadoPagoClient> resolvia a porta com um HttpClient sem BaseAddress.
            services.AddHttpClient<IMercadoPagoClient, MercadoPagoClient>(client =>
            {
                var baseUrl = configuration["MercadoPago:BaseUrl"] ?? "https://api.mercadopago.com/";
                client.BaseAddress = new Uri(baseUrl);
                client.Timeout = TimeSpan.FromSeconds(10); // timeout externo de segurança
            });
        }

        // S27: estorno da ocorrência sobre o EstornarAsync (S32).
        services.AddScoped<IEstornoPedidoGateway, MercadoPagoEstornoPedidoGateway>();

        return services;
    }
}
