using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Infra.Notifications.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;

namespace EasyStock.Infra.Notifications.WhatsApp;

public sealed class MetaCloudWhatsAppProvider(
    IHttpClientFactory httpClientFactory,
    IOptions<MetaCloudWhatsAppOptions> options,
    ILogger<MetaCloudWhatsAppProvider> logger) : IProvedorWhatsApp
{
    public string Nome => "meta";

    private static readonly ResiliencePipeline Pipeline = new ResiliencePipelineBuilder()
        .AddRetry(new RetryStrategyOptions
        {
            MaxRetryAttempts = 3,
            BackoffType = DelayBackoffType.Exponential,
            Delay = TimeSpan.FromSeconds(1),
            ShouldHandle = new PredicateBuilder().Handle<HttpRequestException>()
        })
        .Build();

    public async Task<ResultadoEnvio> EnviarAsync(MensagemPronta mensagem, CancellationToken ct = default)
    {
        var opts = options.Value;
        var sw = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            await Pipeline.ExecuteAsync(async pollyToken =>
            {
                using var client = httpClientFactory.CreateClient("MetaWhatsApp");
                client.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", opts.AccessToken);

                var url = $"{opts.BaseUrl}/{opts.PhoneNumberId}/messages";
                var body = JsonSerializer.Serialize(new
                {
                    messaging_product = "whatsapp",
                    to = mensagem.Destinatario,
                    type = "text",
                    text = new { body = mensagem.Corpo }
                });

                var content = new StringContent(body, Encoding.UTF8, "application/json");
                var response = await client.PostAsync(url, content, pollyToken);
                response.EnsureSuccessStatusCode();
            }, ct);

            sw.Stop();
            return new ResultadoEnvio(Sucesso: true, ProviderUsado: "meta", DuracaoMs: sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            logger.LogError(ex, "Falha Meta WhatsApp para {Destinatario}", mensagem.Destinatario);
            return new ResultadoEnvio(Sucesso: false, ProviderUsado: "meta",
                ErroDetalhado: ex.Message, DuracaoMs: sw.ElapsedMilliseconds);
        }
    }

    /// <summary>
    /// Onda 2.1 — envia mensagem via template aprovado na Meta Business Manager.
    /// Templates exigem aprovacao previa (24-72h); fora da janela de 24h pos-ultima-resposta-do-cliente,
    /// SO templates podem ser enviados (utility/marketing/authentication categories).
    ///
    /// <para>
    /// Variaveis sao posicionais ({{1}}, {{2}}, ...) na ordem definida no template aprovado.
    /// languageCode segue padrao IETF (pt_BR, en_US).
    /// </para>
    /// </summary>
    public async Task<ResultadoEnvio> EnviarTemplateAsync(
        string destino,
        string templateName,
        IReadOnlyList<string> vars,
        string languageCode = "pt_BR",
        CancellationToken ct = default)
    {
        var opts = options.Value;
        var sw = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            await Pipeline.ExecuteAsync(async pollyToken =>
            {
                using var client = httpClientFactory.CreateClient("MetaWhatsApp");
                client.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", opts.AccessToken);

                var url = $"{opts.BaseUrl}/{opts.PhoneNumberId}/messages";
                var parameters = vars.Select(v => new { type = "text", text = v }).ToArray();
                var components = vars.Count > 0
                    ? new object[] { new { type = "body", parameters } }
                    : Array.Empty<object>();

                var body = JsonSerializer.Serialize(new
                {
                    messaging_product = "whatsapp",
                    to = destino,
                    type = "template",
                    template = new
                    {
                        name = templateName,
                        language = new { code = languageCode },
                        components
                    }
                });

                var content = new StringContent(body, Encoding.UTF8, "application/json");
                var response = await client.PostAsync(url, content, pollyToken);
                if (!response.IsSuccessStatusCode)
                {
                    var detalhe = await response.Content.ReadAsStringAsync(pollyToken);
                    throw new InvalidOperationException(
                        $"Meta WhatsApp retornou HTTP {(int)response.StatusCode}: {detalhe}");
                }
            }, ct);

            sw.Stop();
            return new ResultadoEnvio(Sucesso: true, ProviderUsado: "meta:template", DuracaoMs: sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            logger.LogError(ex, "Falha Meta WhatsApp template {Template} para {Destinatario}", templateName, destino);
            return new ResultadoEnvio(Sucesso: false, ProviderUsado: "meta:template",
                ErroDetalhado: ex.Message, DuracaoMs: sw.ElapsedMilliseconds);
        }
    }
}
