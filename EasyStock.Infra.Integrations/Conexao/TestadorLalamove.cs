using System.Net;
using System.Text.Json;
using EasyStock.Application.Ports.Output.Integration.Conexao;
using EasyStock.Application.UseCases.Integracoes;
using EasyStock.Domain.Integration;
using EasyStock.Infra.Integrations.Logistica.Lalamove;

namespace EasyStock.Infra.Integrations.Conexao;

/// <summary>
/// Teste de conexão da Lalamove (F16, #1246): <c>GET /v3/cities</c> com <c>Market: BR</c>, assinado
/// (<see cref="AssinadorLalamove"/>). Só lista cidades: não cota, não chama motorista.
/// </summary>
public sealed class TestadorLalamove(HttpClient http, TimeProvider relogio) : ITestadorIntegracao
{
    public const string HostSandbox = "https://rest.sandbox.lalamove.com";
    public const string HostProducao = "https://rest.lalamove.com";
    private const string Caminho = "/v3/cities";

    public string Provider => CatalogoIntegracoes.Lalamove;

    public async Task<ResultadoTesteIntegracao> TestarAsync(ChaveParaTeste chave, CancellationToken ct = default)
    {
        var apiKey = chave.Campo(CatalogoIntegracoes.CampoApiKey);
        var secret = chave.Campo(CatalogoIntegracoes.CampoApiSecret);
        if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(secret))
            return ResultadoTesteIntegracao.Falhou("Faltam a API key e a secret da Lalamove.");

        var host = chave.Ambiente == AmbienteIntegracao.Sandbox ? HostSandbox : HostProducao;
        var ts = relogio.GetUtcNow().ToUnixTimeMilliseconds();
        using var requisicao = new HttpRequestMessage(HttpMethod.Get, host + Caminho);
        requisicao.Headers.TryAddWithoutValidation("Authorization", AssinadorLalamove.Autorizacao(apiKey, secret, ts, "GET", Caminho, ""));
        requisicao.Headers.Add("Market", "BR");
        requisicao.Headers.Add("Request-ID", Guid.NewGuid().ToString());

        using var resposta = await http.SendAsync(requisicao, ct);
        var corpo = await resposta.Content.ReadAsStringAsync(ct);

        if (resposta.IsSuccessStatusCode)
        {
            var cidades = ContarCidades(corpo);
            return ResultadoTesteIntegracao.Passou(cidades is { } n
                ? $"Chave conferida: {n} cidades atendidas no Brasil."
                : "Chave conferida.");
        }

        var codigo = PrimeiroErro(corpo);
        var sufixo = codigo ?? $"HTTP {(int)resposta.StatusCode}";
        return ResultadoTesteIntegracao.Falhou(resposta.StatusCode switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                $"A Lalamove recusou a chave ou a assinatura ({sufixo}): confira a API key, a secret e o ambiente.",
            HttpStatusCode.TooManyRequests => $"Limite de chamadas da Lalamove atingido ({sufixo}). Tente em 1 minuto.",
            HttpStatusCode.PaymentRequired => $"Conta Lalamove sem saldo ({sufixo}).",
            _ => $"A Lalamove não aceitou a chamada ({sufixo}).",
        });
    }

    private static int? ContarCidades(string corpo)
    {
        try
        {
            using var doc = JsonDocument.Parse(corpo);
            return doc.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array
                ? data.GetArrayLength()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Formato de erro da v3: <c>{"errors":[{"id":"ERR_...","message":"..."}]}</c>. Só o id volta.</summary>
    private static string? PrimeiroErro(string corpo)
    {
        try
        {
            using var doc = JsonDocument.Parse(corpo);
            if (doc.RootElement.TryGetProperty("errors", out var erros) && erros.ValueKind == JsonValueKind.Array
                && erros.GetArrayLength() > 0 && erros[0].TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String)
                return id.GetString();
        }
        catch (JsonException)
        {
            // corpo fora do formato: devolve o status HTTP
        }
        return null;
    }
}
