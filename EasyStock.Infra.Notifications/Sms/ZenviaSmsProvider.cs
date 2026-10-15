using System.Text;
using System.Text.Json;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Infra.Notifications.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EasyStock.Infra.Notifications.Sms;

/// <summary>
/// SMS pela Zenvia. Uma chamada só, como o <see cref="TwilioSmsProvider"/> (N2, #1507): o POST não é idempotente e o
/// retry da Polly podia entregar o SMS duas vezes. Quem repete é o outbox. Pelo status: 4xx (menos 408 e 429) é falha
/// permanente; 408 e 429 são transitórios; 5xx, timeout e queda de conexão são <see cref="DesfechoEnvio.Indeterminado"/>.
/// </summary>
public sealed class ZenviaSmsProvider(
    IHttpClientFactory httpClientFactory,
    IOptions<ZenviaSmsOptions> options,
    ILogger<ZenviaSmsProvider> logger) : IProvedorSms
{
    public string Nome => "zenvia";

    public async Task<ResultadoEnvio> EnviarAsync(MensagemPronta mensagem, CancellationToken ct = default)
    {
        var opts = options.Value;
        var sw = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            using var client = httpClientFactory.CreateClient("ZenviaSms");
            client.DefaultRequestHeaders.Add("X-API-TOKEN", opts.ApiToken);

            var body = JsonSerializer.Serialize(new
            {
                from = opts.From,
                to = mensagem.Destinatario,
                contents = new[] { new { type = "text", text = mensagem.Corpo } }
            });

            using var content = new StringContent(body, Encoding.UTF8, "application/json");
            using var response = await client.PostAsync($"{opts.BaseUrl}/channels/sms/messages", content, ct);
            sw.Stop();

            var status = (int)response.StatusCode;
            if (response.IsSuccessStatusCode)
                return new ResultadoEnvio(Sucesso: true, ProviderUsado: "zenvia", StatusHttp: status, DuracaoMs: sw.ElapsedMilliseconds);

            logger.LogWarning("Zenvia SMS recusou outbox={OutboxId} HTTP {Status}", mensagem.OutboxId, status);
            return ClassificadorDeFalha.DeRespostaHttpDeEnvioUnico("zenvia", status, sw.ElapsedMilliseconds);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw; // o host está parando: não é falha da Zenvia nem timeout
        }
        catch (Exception ex)
        {
            sw.Stop();
            logger.LogError(ex, "Falha Zenvia SMS outbox={OutboxId}", mensagem.OutboxId); // sem telefone: LGPD (#1292)
            return ClassificadorDeFalha.DeExcecaoDeEnvioUnico("zenvia", ex, sw.ElapsedMilliseconds);
        }
    }
}
