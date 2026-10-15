using System.Net.Http.Headers;
using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Infra.Integrations.Meta;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace EasyStock.Infra.Integrations.DependencyInjection;

/// <summary>
/// Messenger e Instagram na porta de canal (S35). Com <c>Atendimento:MetaMensageria:Provider=meta</c> o
/// envio vai à Send API com o page token; senão ao stub. Os adaptadores são registrados sempre, para
/// o console responder também em desenvolvimento.
/// </summary>
public static class MetaMensageriaServiceCollectionExtensions
{
    public static IServiceCollection AddEasyStockMetaMensageria(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MetaMensageriaOptions>(configuration.GetSection(MetaMensageriaOptions.Secao));

        services.AddHttpClient<MetaMensageriaHttpTransporte>("meta-mensageria", (sp, client) =>
        {
            var opts = sp.GetRequiredService<IOptions<MetaMensageriaOptions>>().Value;
            if (!string.IsNullOrWhiteSpace(opts.BaseUrl))
                client.BaseAddress = new Uri(opts.BaseUrl.TrimEnd('/') + "/");
            if (!string.IsNullOrWhiteSpace(opts.PageAccessToken))
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", opts.PageAccessToken);
        });
        services.AddSingleton<StubMetaMensageriaTransporte>();

        var provider = configuration[$"{MetaMensageriaOptions.Secao}:Provider"] ?? "stub";
        if (string.Equals(provider, "meta", StringComparison.OrdinalIgnoreCase))
            services.AddScoped<IMetaMensageriaTransporte>(sp => sp.GetRequiredService<MetaMensageriaHttpTransporte>());
        else
            services.AddScoped<IMetaMensageriaTransporte>(sp => sp.GetRequiredService<StubMetaMensageriaTransporte>());

        services.AddScoped<ICanalMensageria, CanalMessenger>();
        services.AddScoped<ICanalMensageria, CanalInstagram>();

        return services;
    }
}