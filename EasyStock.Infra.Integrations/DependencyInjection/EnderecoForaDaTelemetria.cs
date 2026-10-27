using System.Diagnostics;
using EasyStock.Application.Ports.Output.Lookup;
using EasyStock.Infra.Integrations.Geocoding;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EasyStock.Infra.Integrations.DependencyInjection;

/// <summary>
/// Endereço e CEP do cliente fora do span e do log (I8.2, #1661). ViaCEP, Nominatim, Google e TomTom
/// recebem o dado na URL (caminho ou query), e a URL é o que a instrumentação HTTP do OpenTelemetry
/// grava em <c>url.full</c> e o que o <c>IHttpClientFactory</c> escreve no log de cada requisição.
/// </summary>
public static class EnderecoForaDaTelemetria
{
    /// <summary>Hosts fixos dos provedores; os de base URL configurável entram por <see cref="Hosts"/>.</summary>
    private static readonly string[] HostsConhecidos =
    [
        "viacep.com.br", "nominatim.openstreetmap.org", "maps.googleapis.com", "routes.googleapis.com", "api.tomtom.com",
    ];

    /// <summary>
    /// Nomes dos <c>HttpClient</c> que levam endereço na URL. Cliente tipado leva o nome do tipo de
    /// serviço; os demais, o nome dado no registro. Adapter novo de geocodificação entra aqui.
    /// </summary>
    private static readonly string[] ClientesComEndereco =
    [
        nameof(ICepLookupClient), nameof(IGeocodingClient), nameof(NominatimGeocodingClient),
        nameof(GoogleGeocodingClient), "TomTomBaseUrl",
    ];

    /// <summary>
    /// Tira o log por requisição do <c>IHttpClientFactory</c> dos clientes de CEP e de geocodificação.
    /// O que o próprio adapter loga (status, timeout) continua.
    /// </summary>
    public static IServiceCollection AddEnderecoForaDoLogHttp(this IServiceCollection services)
    {
        foreach (var nome in ClientesComEndereco)
            services.AddHttpClient(nome).RemoveAllLoggers();
        return services;
    }

    /// <summary>
    /// Enriquecedor para <c>AddHttpClientInstrumentation</c>: nas chamadas aos provedores de endereço,
    /// <c>url.full</c> fica só com esquema, host e porta. Também oculta a geração de token do WPPConnect.
    /// </summary>
    public static Action<Activity, HttpRequestMessage> OcultarUrlNoSpan(IConfiguration configuration)
    {
        var hosts = Hosts(configuration);
        Uri.TryCreate(configuration["Atendimento:WppConnect:BaseUrl"] ?? "http://127.0.0.1:21465/",
            UriKind.Absolute, out var wpp);
        return (activity, request) =>
        {
            // O WPPConnect também leva segredo no caminho ao gerar o token da sessão.
            if (request.RequestUri is { } url && (EhHostDeEndereco(hosts, url.Host)
                || wpp is not null && wpp.IsBaseOf(url)
                && url.AbsolutePath.EndsWith("/generate-token", StringComparison.Ordinal)))
                activity.SetTag("url.full", $"{url.Scheme}://{url.Authority}/");
        };
    }

    /// <summary>O host do provedor ou um subdomínio dele (redirecionamento para <c>www.</c>, por exemplo).</summary>
    internal static bool EhHostDeEndereco(HashSet<string> hosts, string host) =>
        hosts.Contains(host) || hosts.Any(h => host.EndsWith("." + h, StringComparison.OrdinalIgnoreCase));

    internal static HashSet<string> Hosts(IConfiguration configuration)
    {
        var hosts = new HashSet<string>(HostsConhecidos, StringComparer.OrdinalIgnoreCase);
        foreach (var chave in new[] { "Storefront:Frete:ViaCepBaseUrl", "Storefront:Frete:NominatimBaseUrl" })
        {
            if (Uri.TryCreate(configuration[chave], UriKind.Absolute, out var url))
                hosts.Add(url.Host);
        }
        return hosts;
    }
}
