using System.Net.Http.Headers;
using System.Text;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Infra.Notifications.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EasyStock.Infra.Notifications.WhatsApp;

/// <summary>
/// WhatsApp pelo Twilio. Uma chamada só (N2): quem repete é o outbox, com backoff de minutos. Pelo status: 4xx
/// (menos 408 e 429) é falha permanente; 408 e 429 são transitórios; 5xx, timeout e queda de conexão são
/// <see cref="DesfechoEnvio.Indeterminado"/>, porque o Twilio pode já ter aceitado a mensagem e repetir duplicaria.
/// </summary>
public sealed class TwilioWhatsAppProvider(
    IHttpClientFactory httpClientFactory,
    IOptions<TwilioWhatsAppOptions> options,
    ILogger<TwilioWhatsAppProvider> logger) : IProvedorWhatsApp
{
    public string Nome => "twilio";

    public async Task<ResultadoEnvio> EnviarAsync(MensagemPronta mensagem, CancellationToken ct = default)
    {
        var opts = options.Value;
        var sw = System.Diagnostics.Stopwatch.StartNew();

        try
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

            using var response = await client.PostAsync(url, content, ct);
            sw.Stop();

            var status = (int)response.StatusCode;
            if (response.IsSuccessStatusCode)
                return new ResultadoEnvio(Sucesso: true, ProviderUsado: "twilio", StatusHttp: status, DuracaoMs: sw.ElapsedMilliseconds);

            logger.LogWarning("Twilio WhatsApp recusou outbox={OutboxId} HTTP {Status}", mensagem.OutboxId, status);
            return ClassificadorDeFalha.DeRespostaHttpDeEnvioUnico("twilio", status, sw.ElapsedMilliseconds);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw; // o host está parando: não é falha do Twilio nem timeout
        }
        catch (Exception ex)
        {
            sw.Stop();
            logger.LogError(ex, "Falha Twilio WhatsApp outbox={OutboxId}", mensagem.OutboxId); // sem telefone: LGPD (#1292)
            return ClassificadorDeFalha.DeExcecaoDeEnvioUnico("twilio", ex, sw.ElapsedMilliseconds);
        }
    }
}
