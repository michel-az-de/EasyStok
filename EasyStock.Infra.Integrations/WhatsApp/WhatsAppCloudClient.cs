using System.Net.Http.Json;
using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Infra.Integrations.Resilience;
using EasyStock.Infra.Integrations.WhatsApp.Dtos;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly.Registry;

namespace EasyStock.Infra.Integrations.WhatsApp;

/// <summary>
/// Cliente real da Cloud API da Meta. Erros de aplicação (código numérico da Meta, ex.: 131047)
/// nunca passam pelo pipeline Polly como exceção — só a chamada HTTP em si é envolvida por ele,
/// então falha permanente da Meta não é reenviada automaticamente (só rede/timeout retry).
/// </summary>
public sealed class WhatsAppCloudClient(
    HttpClient httpClient,
    IOptions<WhatsAppCloudOptions> options,
    ResiliencePipelineProvider<string> pipelineProvider,
    ILogger<WhatsAppCloudClient> logger) : IWhatsAppCloudClient
{
    private readonly WhatsAppCloudOptions _options = options.Value;

    private static readonly HashSet<int> CodigosPermanentes = [131047, 131026, 100];

    public Task<EnvioWhatsAppResult> EnviarTextoAsync(
        string waId, string texto, string? responderAWamid = null, CancellationToken ct = default)
    {
        object payload = responderAWamid is null
            ? new { messaging_product = "whatsapp", to = waId, type = "text", text = new { body = texto } }
            : new
            {
                messaging_product = "whatsapp",
                to = waId,
                type = "text",
                text = new { body = texto },
                context = new { message_id = responderAWamid }
            };

        return EnviarEExtrairWamidAsync(payload, ct);
    }

    public Task<EnvioWhatsAppResult> EnviarImagemAsync(
        string waId, string urlPublica, string? legenda = null, CancellationToken ct = default)
    {
        var payload = new
        {
            messaging_product = "whatsapp",
            to = waId,
            type = "image",
            image = new { link = urlPublica, caption = legenda }
        };

        return EnviarEExtrairWamidAsync(payload, ct);
    }

    public Task<EnvioWhatsAppResult> EnviarBotoesAsync(
        string waId, string corpo, IReadOnlyList<(string Id, string Titulo)> botoes, CancellationToken ct = default)
    {
        if (botoes.Count is 0 or > 3)
            throw new ArgumentException("EnviarBotoesAsync aceita de 1 a 3 botões.", nameof(botoes));

        foreach (var (id, titulo) in botoes)
        {
            if (titulo.Length > 20)
                throw new ArgumentException($"Título de botão excede 20 caracteres: \"{titulo}\".", nameof(botoes));
            if (id.Length > 256)
                throw new ArgumentException($"Id de botão excede 256 caracteres: \"{id}\".", nameof(botoes));
        }

        var payload = new
        {
            messaging_product = "whatsapp",
            to = waId,
            type = "interactive",
            interactive = new
            {
                type = "button",
                body = new { text = corpo },
                action = new
                {
                    buttons = botoes.Select(b => new { type = "reply", reply = new { id = b.Id, title = b.Titulo } }).ToArray()
                }
            }
        };

        return EnviarEExtrairWamidAsync(payload, ct);
    }

    public Task<EnvioWhatsAppResult> EnviarTemplateAsync(
        string waId,
        string nomeTemplate,
        string idioma,
        IReadOnlyList<string> parametrosCorpo,
        IReadOnlyList<(string Id, string Titulo)>? botoesQuickReply = null,
        CancellationToken ct = default)
    {
        var components = new List<object>();
        if (parametrosCorpo.Count > 0)
        {
            components.Add(new
            {
                type = "body",
                parameters = parametrosCorpo.Select(p => new { type = "text", text = p }).ToArray()
            });
        }

        if (botoesQuickReply is { Count: > 0 })
        {
            for (var i = 0; i < botoesQuickReply.Count; i++)
            {
                components.Add(new
                {
                    type = "button",
                    sub_type = "quick_reply",
                    index = i.ToString(),
                    parameters = new object[] { new { type = "payload", payload = botoesQuickReply[i].Id } }
                });
            }
        }

        var payload = new
        {
            messaging_product = "whatsapp",
            to = waId,
            type = "template",
            template = new { name = nomeTemplate, language = new { code = idioma }, components }
        };

        return EnviarEExtrairWamidAsync(payload, ct);
    }

    public async Task MarcarComoLidaAsync(string wamid, CancellationToken ct = default)
    {
        var payload = new { messaging_product = "whatsapp", status = "read", message_id = wamid };
        using var response = await PostMessagesAsync(payload, ct);
        if (!response.IsSuccessStatusCode)
            await LancarErroAsync(response, ct);
    }

    public async Task<(Stream Conteudo, string MimeType)> BaixarMidiaAsync(string mediaId, CancellationToken ct = default)
    {
        var pipeline = pipelineProvider.GetPipeline(IntegrationCategories.WhatsApp);

        using var metadataResponse = await pipeline.ExecuteAsync(
            async pollyCt => await httpClient.GetAsync(mediaId, pollyCt), ct);
        if (!metadataResponse.IsSuccessStatusCode)
            await LancarErroAsync(metadataResponse, ct);

        var metadata = await metadataResponse.Content.ReadFromJsonAsync<MetaMediaMetadata>(cancellationToken: ct)
            ?? throw new WhatsAppCloudException(0, "Meta não devolveu metadados da mídia.", ehPermanente: true);

        var binarioResponse = await pipeline.ExecuteAsync(
            async pollyCt => await httpClient.GetAsync(metadata.Url, pollyCt), ct);
        if (!binarioResponse.IsSuccessStatusCode)
            await LancarErroAsync(binarioResponse, ct);

        var conteudo = await binarioResponse.Content.ReadAsStreamAsync(ct);
        return (conteudo, metadata.MimeType);
    }

    private async Task<EnvioWhatsAppResult> EnviarEExtrairWamidAsync(object payload, CancellationToken ct)
    {
        using var response = await PostMessagesAsync(payload, ct);
        if (!response.IsSuccessStatusCode)
            await LancarErroAsync(response, ct);

        var body = await response.Content.ReadFromJsonAsync<MetaSendMessageResponse>(cancellationToken: ct);
        var wamid = body?.Messages?.FirstOrDefault()?.Id;
        if (string.IsNullOrEmpty(wamid))
            throw new WhatsAppCloudException(0, "Meta retornou 2xx sem message id.", ehPermanente: true);

        return new EnvioWhatsAppResult(wamid);
    }

    private Task<HttpResponseMessage> PostMessagesAsync(object payload, CancellationToken ct)
    {
        var pipeline = pipelineProvider.GetPipeline(IntegrationCategories.WhatsApp);
        var url = $"{_options.PhoneNumberId}/messages";
        return pipeline.ExecuteAsync(async pollyCt => await httpClient.PostAsJsonAsync(url, payload, pollyCt), ct).AsTask();
    }

    private async Task LancarErroAsync(HttpResponseMessage response, CancellationToken ct)
    {
        MetaErrorEnvelope? envelope = null;
        try
        {
            envelope = await response.Content.ReadFromJsonAsync<MetaErrorEnvelope>(cancellationToken: ct);
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or NotSupportedException)
        {
            // Corpo de erro não veio no formato esperado — segue com código 0 abaixo.
        }

        var erro = envelope?.Error;
        var codigo = erro?.Code ?? 0;
        var mensagem = erro?.Message ?? $"Meta WhatsApp retornou HTTP {(int)response.StatusCode}.";
        logger.LogWarning("Falha WhatsApp Cloud API: codigo={Codigo} mensagem={Mensagem}", codigo, mensagem);
        throw new WhatsAppCloudException(codigo, mensagem, CodigosPermanentes.Contains(codigo));
    }
}
