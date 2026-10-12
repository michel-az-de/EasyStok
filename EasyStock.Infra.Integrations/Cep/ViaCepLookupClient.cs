using System.Net.Http.Json;
using System.Text.Json.Serialization;
using EasyStock.Application.Ports.Output.Lookup;
using Microsoft.Extensions.Logging;

namespace EasyStock.Infra.Integrations.Cep;

/// <summary>
/// Adapter HTTP para o <see href="https://viacep.com.br/">ViaCEP</see> — serviço
/// público brasileiro que mapeia CEP → endereço (logradouro, bairro, cidade, UF).
///
/// <para>
/// <strong>Contrato</strong>: nunca lança. Timeout, 4xx, 5xx ou JSON inválido
/// viram <see langword="null"/> (best-effort). Use case de frete trata null
/// como "sem bairro" e segue pelo CEP.
/// </para>
///
/// <para>
/// <strong>Timeout</strong>: 1s por chamada. O endpoint de frete precisa
/// responder em &lt;500ms na maior parte do tempo, então o lookup tem cap
/// agressivo. <c>HttpClient.Timeout</c> é configurado pela DI extension
/// (<c>CepServiceCollectionExtensions</c>).
/// </para>
///
/// <para>
/// <strong>CEP não encontrado</strong>: ViaCEP retorna 200 + <c>{"erro":true}</c>.
/// Tratado como null. CEP em formato inválido retorna 400 — também null.
/// </para>
/// </summary>
public sealed class ViaCepLookupClient : ICepLookupClient
{
    private readonly HttpClient _http;
    private readonly ILogger<ViaCepLookupClient> _logger;

    public ViaCepLookupClient(HttpClient http, ILogger<ViaCepLookupClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<CepLookupResult?> LookupAsync(string cep, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(cep))
            return null;

        try
        {
            // ViaCEP exige 8 dígitos puros. /ws/{cep}/json/
            var url = $"ws/{cep}/json/";
            using var resp = await _http.GetAsync(url, ct);
            if (!resp.IsSuccessStatusCode)
            {
                // O CEP não vai para o log (I8.2, #1661): basta o status para diagnosticar.
                _logger.LogDebug("ViaCEP retornou {Status}", (int)resp.StatusCode);
                return null;
            }

            var payload = await resp.Content.ReadFromJsonAsync<ViaCepPayload>(ct);
            if (payload is null || payload.Erro == true)
                return null;

            return new CepLookupResult(
                Cep: cep,
                Logradouro: payload.Logradouro,
                Bairro: payload.Bairro,
                Cidade: payload.Localidade,
                Uf: payload.Uf);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Timeout do HttpClient → OperationCanceledException sem ct cancelado.
            _logger.LogDebug("ViaCEP timeout");
            return null;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogDebug("ViaCEP HTTP falhou: {Status}", ex.StatusCode);
            return null;
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException)
        {
            _logger.LogDebug("ViaCEP JSON inválido: {Excecao}", ex.GetType().Name);
            return null;
        }
    }

    /// <summary>Shape do payload do ViaCEP — campos relevantes.</summary>
    private sealed record ViaCepPayload
    {
        [JsonPropertyName("logradouro")] public string? Logradouro { get; init; }
        [JsonPropertyName("bairro")] public string? Bairro { get; init; }
        [JsonPropertyName("localidade")] public string? Localidade { get; init; }
        [JsonPropertyName("uf")] public string? Uf { get; init; }
        [JsonPropertyName("erro")] public bool? Erro { get; init; }
    }
}
