using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using EasyStock.Application.Ports.Output.Integration.Conexao;
using EasyStock.Application.UseCases.Integracoes;
using EasyStock.Infra.Integrations.WhatsApp;
using Microsoft.Extensions.Options;

namespace EasyStock.Infra.Integrations.Conexao;

/// <summary>
/// Teste de conexão do WhatsApp (F16, #1246): <c>GET /{phone-number-id}?fields=display_phone_number,
/// verified_name,quality_rating</c> na Graph API, com o token global da app da FMA. Só lê o número:
/// nenhuma mensagem sai. O id entra no caminho só se for número (nunca muda o recurso chamado).
/// </summary>
public sealed partial class TestadorWhatsApp(HttpClient http, IOptions<WhatsAppCloudOptions> opcoes) : ITestadorIntegracao
{
    public string Provider => CatalogoIntegracoes.WhatsApp;

    [GeneratedRegex("^[0-9]{5,30}$")]
    private static partial Regex FormatoPhoneNumberId();

    public async Task<ResultadoTesteIntegracao> TestarAsync(ChaveParaTeste chave, CancellationToken ct = default)
    {
        var token = chave.Campo(CatalogoIntegracoes.CampoAccessToken);
        var numero = chave.Campo(CatalogoIntegracoes.CampoPhoneNumberId);
        if (string.IsNullOrWhiteSpace(token))
            return ResultadoTesteIntegracao.Falhou("O token da Meta não está configurado. Fale com a FMA.");
        if (string.IsNullOrWhiteSpace(numero) || !FormatoPhoneNumberId().IsMatch(numero))
            return ResultadoTesteIntegracao.Falhou("O número de WhatsApp da loja não está vinculado. Fale com a FMA.");

        var baseUrl = opcoes.Value.BaseUrl.TrimEnd('/');
        using var requisicao = new HttpRequestMessage(HttpMethod.Get,
            $"{baseUrl}/{numero}?fields=display_phone_number,verified_name,quality_rating");
        requisicao.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var resposta = await http.SendAsync(requisicao, ct);
        var corpo = await resposta.Content.ReadAsStringAsync(ct);

        if (resposta.IsSuccessStatusCode)
        {
            var (exibido, nome, qualidade) = LerNumero(corpo);
            var texto = $"Número {exibido ?? numero}{(nome is null ? "" : $" ({nome})")} conferido";
            return ResultadoTesteIntegracao.Passou(qualidade is null ? texto + "." : $"{texto}, qualidade {TraduzirQualidade(qualidade)}.");
        }

        var codigo = CodigoErro(corpo);
        return ResultadoTesteIntegracao.Falhou(codigo switch
        {
            190 => "O token da Meta expirou ou foi revogado (190). Fale com a FMA.",
            100 => "A Meta não achou o número da loja (100): confira o vínculo com a FMA.",
            10 or 200 => $"O token da Meta não tem permissão para este número ({codigo}). Fale com a FMA.",
            4 or 17 or 32 or 613 or 80007 => $"Limite de chamadas da Meta atingido ({codigo}). Tente mais tarde.",
            { } outro => $"A Meta recusou a chamada (código {outro}).",
            null => $"A Meta recusou a chamada (HTTP {(int)resposta.StatusCode}).",
        });
    }

    private static string TraduzirQualidade(string qualidade) => qualidade.ToUpperInvariant() switch
    {
        "GREEN" => "alta",
        "YELLOW" => "média",
        "RED" => "baixa (risco de bloqueio)",
        _ => qualidade,
    };

    private static (string? Exibido, string? Nome, string? Qualidade) LerNumero(string corpo)
    {
        try
        {
            using var doc = JsonDocument.Parse(corpo);
            var raiz = doc.RootElement;
            return (Texto(raiz, "display_phone_number"), Texto(raiz, "verified_name"), Texto(raiz, "quality_rating"));
        }
        catch (JsonException)
        {
            return (null, null, null);
        }
    }

    private static string? Texto(JsonElement e, string campo) =>
        e.TryGetProperty(campo, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    /// <summary>Erro da Graph API: <c>{"error":{"code":190,...}}</c>. A mensagem (em inglês) não volta.</summary>
    private static int? CodigoErro(string corpo)
    {
        try
        {
            using var doc = JsonDocument.Parse(corpo);
            return doc.RootElement.TryGetProperty("error", out var erro) && erro.TryGetProperty("code", out var codigo)
                && codigo.TryGetInt32(out var n) ? n : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
