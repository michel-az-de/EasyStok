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
/// <c>Atendimento:WhatsApp:Cliente=meta</c> (#1102) ou <c>Notifications:WhatsApp:Provider=meta</c>
/// (o switch antigo), senão <see cref="StubWhatsAppCloudClient"/>. A chave do atendimento liga o
/// cliente real sem trocar o provider de notificações.
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

        if (UsaClienteMeta(configuration[ChaveClienteAtendimento], configuration[ChaveProviderNotificacoes]))
            services.AddScoped<IWhatsAppCloudClient>(sp => sp.GetRequiredService<WhatsAppCloudClient>());
        else
            services.AddScoped<IWhatsAppCloudClient>(sp => sp.GetRequiredService<StubWhatsAppCloudClient>());

        // Porta de canal (S34, ADR-0051): o WhatsApp e um dos adaptadores que o ResolvedorCanal escolhe.
        services.AddScoped<ICanalMensageria, CanalWhatsApp>();

        return services;
    }

    /// <summary>Chave própria do cliente da Cloud API do atendimento: <c>meta</c> ou <c>stub</c>.</summary>
    public const string ChaveClienteAtendimento = "Atendimento:WhatsApp:Cliente";

    /// <summary>Chave antiga, do provider de notificações; com <c>meta</c> também leva o cliente real.</summary>
    public const string ChaveProviderNotificacoes = "Notifications:WhatsApp:Provider";

    /// <summary>
    /// Núcleo puro da escolha real/stub: real quando <paramref name="cliente"/> é <c>meta</c> OU
    /// quando <paramref name="provider"/> é <c>meta</c>. O segundo caso não é só fallback: o
    /// <c>MetaCloudWhatsAppProvider</c> das notificações envia por este mesmo cliente, então
    /// <c>Cliente=stub</c> com <c>Provider=meta</c> engoliria as notificações em silêncio. Mesma
    /// regra que o <c>StartupHardening</c> usa para exigir as credenciais da Meta.
    /// </summary>
    public static bool UsaClienteMeta(string? cliente, string? provider)
        => EhMeta(cliente) || EhMeta(provider);

    private static bool EhMeta(string? valor)
        => string.Equals(valor?.Trim(), "meta", StringComparison.OrdinalIgnoreCase);
}
