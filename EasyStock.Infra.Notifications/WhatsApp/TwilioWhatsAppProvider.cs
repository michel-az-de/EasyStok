using System.Net.Http.Headers;
using System.Text;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Infra.Notifications.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;

namespace EasyStock.Infra.Notifications.WhatsApp;

public sealed class TwilioWhatsAppProvider(
    IHttpClientFactory httpClientFactory,
    IOptions<TwilioWhatsAppOptions> options,
    ILogger<TwilioWhatsAppProvider> logger) : IProvedorWhatsApp
{
    public string Nome => "twilio";

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
                using var client = httpClientFactory.CreateClient("TwilioWhatsApp");

                var credentials = Convert.ToBase64String(
                    Encoding.UTF8.GetBytes($"{opts.AccountSid}:{opts.AuthToken}"));
                client.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Basic", credentials);

                var url = $"https://api.twilio.com/2010-04-01/Accounts/{opts.AccountSid}/Messages.json";
                var content = new FormUrlEncodedContent([
                    new("To", $"whatsapp:{mensagem.Destinatario}"),
                    new("From", opts.From),
                    new("Body", mensagem.Corpo)
                ]);

                var response = await client.PostAsync(url, content, pollyToken);
                response.EnsureSuccessStatusCode();
            }, ct);

            sw.Stop();
            return new ResultadoEnvio(Sucesso: true, ProviderUsado: "twilio", DuracaoMs: sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            logger.LogError(ex, "Falha Twilio WhatsApp para {Destinatario}", mensagem.Destinatario);
            return new ResultadoEnvio(Sucesso: false, ProviderUsado: "twilio",
                ErroDetalhado: ex.Message, DuracaoMs: sw.ElapsedMilliseconds);
        }
    }
}
