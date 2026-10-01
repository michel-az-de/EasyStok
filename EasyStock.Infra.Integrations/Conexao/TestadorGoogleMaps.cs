using EasyStock.Application.Ports.Output.Integration.Conexao;
using EasyStock.Application.Ports.Output.Lookup;
using EasyStock.Application.UseCases.Integracoes;
using EasyStock.Infra.Integrations.Geocoding;
using Microsoft.Extensions.Logging;

namespace EasyStock.Infra.Integrations.Conexao;

/// <summary>
/// Teste de conexão do Google Maps (F16, #1246): uma geocodificação pelo
/// <see cref="GoogleGeocodingClient"/>, que já distingue chave recusada de cota estourada. O
/// endereço é fixo (a loja guarda só a coordenada da cozinha, não o endereço): o que se testa é a
/// chave, não o endereço. Uma consulta por teste, dentro da cota gratuita.
/// </summary>
public sealed class TestadorGoogleMaps(HttpClient http, ILoggerFactory logs) : ITestadorIntegracao
{
    private static readonly GeocodeQuery Referencia = new(
        Logradouro: "Praça da Sé", Numero: null, Bairro: "Sé", Cidade: "São Paulo", Uf: "SP", Cep: "01001000");

    public string Provider => CatalogoIntegracoes.GoogleMaps;

    public async Task<ResultadoTesteIntegracao> TestarAsync(ChaveParaTeste chave, CancellationToken ct = default)
    {
        var apiKey = chave.Campo(CatalogoIntegracoes.CampoApiKey);
        if (string.IsNullOrWhiteSpace(apiKey))
            return ResultadoTesteIntegracao.Falhou("Falta a chave do Google Maps.");

        var cliente = new GoogleGeocodingClient(http, apiKey, logs.CreateLogger<GoogleGeocodingClient>());
        var status = await cliente.ObterStatusAsync(Referencia, ct);

        return status switch
        {
            "OK" or "ZERO_RESULTS" => ResultadoTesteIntegracao.Passou("Chave conferida: a geocodificação respondeu."),
            "REQUEST_DENIED" => ResultadoTesteIntegracao.Falhou(
                "O Google recusou a chave (REQUEST_DENIED): confira se a Geocoding API está ligada e as restrições da chave."),
            "OVER_QUERY_LIMIT" or "OVER_DAILY_LIMIT" => ResultadoTesteIntegracao.Falhou(
                $"A cota do Google Maps estourou ({status}): confira o faturamento da conta Google."),
            "ERRO_REDE" => ResultadoTesteIntegracao.Falhou("Não foi possível falar com o Google Maps."),
            _ => ResultadoTesteIntegracao.Falhou($"O Google Maps não aceitou a chamada ({status})."),
        };
    }
}
