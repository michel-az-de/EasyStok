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
            await Pipeline.ExecuteAsync(async token =>
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
                var response = await client.PostAsync(url, content, token);
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
}
