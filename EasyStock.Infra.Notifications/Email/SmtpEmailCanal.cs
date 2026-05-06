using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Domain.Enums.Notifications;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;

namespace EasyStock.Infra.Notifications.Email;

public sealed class SmtpEmailCanal(
    IEmailService emailService,
    ILogger<SmtpEmailCanal> logger) : ICanalNotificacao
{
    public CanalNotificacao Canal => CanalNotificacao.Email;

    private static readonly ResiliencePipeline Pipeline = new ResiliencePipelineBuilder()
        .AddRetry(new RetryStrategyOptions
        {
            MaxRetryAttempts = 3,
            BackoffType = DelayBackoffType.Exponential,
            Delay = TimeSpan.FromSeconds(2),
            UseJitter = true
        })
        .Build();

    public async Task<ResultadoEnvio> EnviarAsync(MensagemPronta mensagem, CancellationToken ct = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            await Pipeline.ExecuteAsync(async token =>
            {
                await emailService.SendAsync(
                    mensagem.Destinatario,
                    mensagem.Assunto,
                    mensagem.Corpo,
                    isHtml: true);
            }, ct);

            sw.Stop();
            logger.LogInformation(
                "Email enviado para {Destinatario} outbox={OutboxId} em {Ms}ms",
                mensagem.Destinatario, mensagem.OutboxId, sw.ElapsedMilliseconds);

            return new ResultadoEnvio(
                Sucesso: true,
                ProviderUsado: "smtp",
                DuracaoMs: sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            logger.LogError(ex,
                "Falha ao enviar email para {Destinatario} outbox={OutboxId}",
                mensagem.Destinatario, mensagem.OutboxId);

            return new ResultadoEnvio(
                Sucesso: false,
                ProviderUsado: "smtp",
                ErroDetalhado: ex.Message,
                DuracaoMs: sw.ElapsedMilliseconds);
        }
    }
}
