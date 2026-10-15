using EasyStock.Application.Ports.Output.Integration.Conexao;
using EasyStock.Application.UseCases.Integracoes;
using EasyStock.Infra.Integrations.DependencyInjection;
using Microsoft.Extensions.Configuration;

namespace EasyStock.Infra.Integrations.Conexao;

/// <summary>
/// Chaves globais da FMA lidas da configuração (F16, #1246), as mesmas que os fluxos já usam:
/// <list type="bullet">
///   <item>Mercado Pago: <c>MercadoPago:AccessToken</c> (fora do stub).</item>
///   <item>Google Maps: <c>Storefront:Frete:GoogleMapsApiKey</c> ou <c>GOOGLE_MAPS_API_KEY</c>.</item>
///   <item>WhatsApp: <c>Notifications:WhatsApp:Meta:AccessToken</c>, com o cliente real da Meta ligado.</item>
/// </list>
/// Lalamove não tem global: é sempre da loja.
/// </summary>
public sealed class ChavesGlobaisIntegracao(IConfiguration configuracao) : IChavesGlobaisIntegracao
{
    public IReadOnlyDictionary<string, string>? Obter(string provider) => provider switch
    {
        CatalogoIntegracoes.MercadoPago when !configuracao.GetValue<bool>("MercadoPago:UseStub") =>
            Campo(CatalogoIntegracoes.CampoAccessToken, configuracao["MercadoPago:AccessToken"]),
        CatalogoIntegracoes.GoogleMaps =>
            Campo(CatalogoIntegracoes.CampoApiKey,
                configuracao[GeocodingServiceCollectionExtensions.GoogleApiKeyKey] is { Length: > 0 } chave
                    ? chave
                    : Environment.GetEnvironmentVariable(GeocodingServiceCollectionExtensions.GoogleApiKeyEnvVar)),
        CatalogoIntegracoes.WhatsApp when WhatsAppCloudClientServiceCollectionExtensions.UsaClienteMeta(
                configuracao[WhatsAppCloudClientServiceCollectionExtensions.ChaveClienteAtendimento],
                configuracao[WhatsAppCloudClientServiceCollectionExtensions.ChaveProviderNotificacoes]) =>
            Campo(CatalogoIntegracoes.CampoAccessToken, configuracao["Notifications:WhatsApp:Meta:AccessToken"]),
        _ => null,
    };

    private static Dictionary<string, string>? Campo(string nome, string? valor) =>
        string.IsNullOrWhiteSpace(valor) || valor.Contains("${", StringComparison.Ordinal)
            ? null
            : new Dictionary<string, string> { [nome] = valor.Trim() };
}
