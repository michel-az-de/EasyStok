using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Infra.Integrations.WhatsApp.Dtos;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EasyStock.Infra.Integrations.WhatsApp;

/// <summary>
/// Graph API do Embedded Signup v4 com coexistência (#1417). HttpClient próprio, sem o Bearer global: cada chamada
/// leva o business token da empresa no cabeçalho, e a troca do <c>code</c> leva o segredo do app na query. Sem retry:
/// o <c>code</c> vale uma vez só, e repetir um POST aceito não ajuda. Nem o token, nem o <c>code</c>, nem o segredo
/// vão para o log; o .NET já mascara a query no log do HttpClient.
/// </summary>
public sealed class MetaEmbeddedSignupClient(
    HttpClient httpClient,
    IOptions<WhatsAppCloudOptions> options,
    ILogger<MetaEmbeddedSignupClient> logger) : IMetaEmbeddedSignupClient
{
    public const string NomeHttpClient = "meta-embedded-signup";

    private readonly WhatsAppCloudOptions _options = options.Value;

    public async Task<string> TrocarCodigoPorTokenAsync(string code, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_options.AppId) || string.IsNullOrWhiteSpace(_options.AppSecret))
            throw new WhatsAppCloudException(0,
                "Notifications:WhatsApp:Meta:AppId e AppSecret precisam estar configurados para conectar o WhatsApp.",
                ehPermanente: true);

        var url = "oauth/access_token"
                  + $"?client_id={Uri.EscapeDataString(_options.AppId.Trim())}"
                  + $"&client_secret={Uri.EscapeDataString(_options.AppSecret.Trim())}"
                  + $"&code={Uri.EscapeDataString(code.Trim())}";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using var json = await EnviarAsync(request, "troca do code", ct);
        var token = Texto(json.RootElement, "access_token");
        if (string.IsNullOrWhiteSpace(token))
            throw new WhatsAppCloudException(0, "A Meta não devolveu o access_token na troca do code.", ehPermanente: true);
        return token;
    }

    public async Task InscreverAppNaWabaAsync(string wabaId, string token, CancellationToken ct = default)
    {
        using var request = ComToken(HttpMethod.Post, $"{Uri.EscapeDataString(wabaId)}/subscribed_apps", token);
        using var json = await EnviarAsync(request, "inscrição do app na WABA", ct);
        if (json.RootElement.TryGetProperty("success", out var ok) && ok.ValueKind == JsonValueKind.False)
            throw new WhatsAppCloudException(0, "A Meta recusou a inscrição do app na WABA (success=false).", ehPermanente: true);
    }

    public async Task<NumeroWhatsAppMeta> ConsultarNumeroAsync(string phoneNumberId, string token, CancellationToken ct = default)
    {
        using var request = ComToken(HttpMethod.Get,
            $"{Uri.EscapeDataString(phoneNumberId)}?fields=display_phone_number,verified_name,is_on_biz_app,platform_type", token);
        using var json = await EnviarAsync(request, "consulta do número", ct);
        var raiz = json.RootElement;
        bool? noApp = raiz.TryGetProperty("is_on_biz_app", out var b) && b.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? b.GetBoolean()
            : null;
        return new NumeroWhatsAppMeta(
            Texto(raiz, "display_phone_number"), Texto(raiz, "verified_name"), noApp, Texto(raiz, "platform_type"));
    }

    public async Task<string?> SolicitarSincronizacaoAsync(
        string phoneNumberId, string token, TipoSincronizacaoWhatsApp tipo, CancellationToken ct = default)
    {
        var syncType = tipo == TipoSincronizacaoWhatsApp.Historico ? "history" : "smb_app_state_sync";
        using var request = ComToken(HttpMethod.Post, $"{Uri.EscapeDataString(phoneNumberId)}/smb_app_data", token);
        request.Content = JsonContent.Create(new { messaging_product = "whatsapp", sync_type = syncType });
        using var json = await EnviarAsync(request, $"sincronização {syncType}", ct);
        return Texto(json.RootElement, "request_id");
    }

    private static HttpRequestMessage ComToken(HttpMethod metodo, string url, string token)
    {
        var request = new HttpRequestMessage(metodo, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    /// <summary>Envia e devolve o JSON da resposta; erro da Meta vira <see cref="WhatsAppCloudException"/> com o código dela.</summary>
    private async Task<JsonDocument> EnviarAsync(HttpRequestMessage request, string etapa, CancellationToken ct)
    {
        using var response = await httpClient.SendAsync(request, ct);
        var corpo = await response.Content.ReadAsStringAsync(ct);

        if (response.IsSuccessStatusCode)
        {
            try
            {
                return JsonDocument.Parse(string.IsNullOrWhiteSpace(corpo) ? "{}" : corpo);
            }
            catch (JsonException)
            {
                throw new WhatsAppCloudException(0, $"A Meta respondeu à {etapa} sem JSON.", ehPermanente: false, (int)response.StatusCode);
            }
        }

        MetaError? erro = null;
        try
        {
            erro = JsonSerializer.Deserialize<MetaErrorEnvelope>(corpo, new JsonSerializerOptions(JsonSerializerDefaults.Web))?.Error;
        }
        catch (JsonException)
        {
            // Corpo fora do formato da Meta: segue com o status HTTP.
        }

        var status = (int)response.StatusCode;
        var mensagem = erro?.Message ?? $"Meta retornou HTTP {status}.";
        // Só código, status e mensagem da Meta: a URL tem o segredo do app e o code, e o cabeçalho tem o token.
        logger.LogWarning("Embedded Signup: falha na {Etapa}: http={Status} codigo={Codigo} mensagem={Mensagem}",
            etapa, status, erro?.Code ?? 0, mensagem);
        throw new WhatsAppCloudException(erro?.Code ?? 0, $"Falha na {etapa}: {mensagem}", ehPermanente: status < 500, status);
    }

    private static string? Texto(JsonElement elemento, string propriedade) =>
        elemento.ValueKind == JsonValueKind.Object
        && elemento.TryGetProperty(propriedade, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;
}
