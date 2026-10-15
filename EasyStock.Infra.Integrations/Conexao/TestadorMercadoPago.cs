using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using EasyStock.Application.Ports.Output.Integration.Conexao;
using EasyStock.Application.UseCases.Integracoes;

namespace EasyStock.Infra.Integrations.Conexao;

/// <summary>
/// Teste de conexão do Mercado Pago (F16, #1246): <c>GET https://api.mercadolibre.com/users/me</c>
/// com o access token no cabeçalho. É o endpoint documentado na página "Credenciais" do Mercado
/// Pago (a mesma conta serve ao Mercado Livre); <c>api.mercadopago.com/users/me</c> não aparece na
/// documentação. Só lê os dados da conta: não cria preferência nem cobra.
/// </summary>
public sealed class TestadorMercadoPago(HttpClient http) : ITestadorIntegracao
{
    public const string UrlUsuario = "https://api.mercadolibre.com/users/me";

    public string Provider => CatalogoIntegracoes.MercadoPago;

    public async Task<ResultadoTesteIntegracao> TestarAsync(ChaveParaTeste chave, CancellationToken ct = default)
    {
        var token = chave.Campo(CatalogoIntegracoes.CampoAccessToken);
        if (string.IsNullOrWhiteSpace(token))
            return ResultadoTesteIntegracao.Falhou("Falta o Access Token do Mercado Pago.");

        using var requisicao = new HttpRequestMessage(HttpMethod.Get, UrlUsuario);
        requisicao.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var resposta = await http.SendAsync(requisicao, ct);

        if (resposta.IsSuccessStatusCode)
        {
            var conta = await ApelidoAsync(resposta, ct);
            return ResultadoTesteIntegracao.Passou(conta is null ? "Conta conferida." : $"Conta conferida: {conta}.");
        }

        var codigo = (int)resposta.StatusCode;
        return ResultadoTesteIntegracao.Falhou(resposta.StatusCode switch
        {
            HttpStatusCode.Unauthorized =>
                $"O Mercado Pago recusou o token ({codigo}): confira se é o Access Token de produção e se não foi revogado.",
            HttpStatusCode.Forbidden => $"O Mercado Pago recusou o token ({codigo}): a aplicação não tem permissão.",
            HttpStatusCode.TooManyRequests => $"Limite de chamadas do Mercado Pago atingido ({codigo}). Tente em 1 minuto.",
            _ => $"O Mercado Pago não aceitou a chamada (HTTP {codigo}).",
        });
    }

    private static async Task<string?> ApelidoAsync(HttpResponseMessage resposta, CancellationToken ct)
    {
        try
        {
            using var doc = await JsonDocument.ParseAsync(await resposta.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            return doc.RootElement.TryGetProperty("nickname", out var apelido) && apelido.ValueKind == JsonValueKind.String
                ? apelido.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
