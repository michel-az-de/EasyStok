using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Infra.Notifications.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;

namespace EasyStock.Infra.Notifications.Sms;

public sealed class ZenviaSmsProvider(
    IHttpClientFactory httpClientFactory,
    IOptions<ZenviaSmsOptions> options,
    ILogger<ZenviaSmsProvider> logger) : IProvedorSms
{
    public string Nome => "zenvia";

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
                using var client = httpClientFactory.CreateClient("ZenviaSms");
                client.DefaultRequestHeaders.Add("X-API-TOKEN", opts.ApiToken);

                var body = JsonSerializer.Serialize(new
                {
                    from = opts.From,
                    to = mensagem.Destinatario,
                    contents = new[] { new { type = "text", text = mensagem.Corpo } }
                });

                var content = new StringContent(body, Encoding.UTF8, "application/json");
                var response = await client.PostAsync(
                    $"{opts.BaseUrl}/channels/sms/messages", content, pollyToken);
                response.EnsureSuccessStatusCode();
            }, ct);

            sw.Stop();
            return new ResultadoEnvio(Sucesso: true, ProviderUsado: "zenvia", DuracaoMs: sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            logger.LogError(ex, "Falha Zenvia SMS para {Destinatario}", mensagem.Destinatario);
            return new ResultadoEnvio(Sucesso: false, ProviderUsado: "zenvia",
                ErroDetalhado: ex.Message, DuracaoMs: sw.ElapsedMilliseconds);
        }
    }
}
