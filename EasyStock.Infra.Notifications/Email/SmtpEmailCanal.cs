using System.Net.Mail;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Domain.Enums.Notifications;
using Microsoft.Extensions.Logging;

namespace EasyStock.Infra.Notifications.Email;

/// <summary>
/// Canal de e-mail do outbox. Uma tentativa por chamada: quem repete é o outbox (backoff de 1, 5 e 30 min), nunca o
/// canal nem o <see cref="IEmailService"/>. Antes eram três camadas aninhadas (Polly do canal, laço do serviço e
/// outbox), até 36 tentativas SMTP por mensagem (N2).
/// <list type="bullet">
/// <item>Sobre um <see cref="IEmailServiceSimulado"/> (o console do desenvolvimento), nada sai: devolve
/// <see cref="DesfechoEnvio.Simulado"/> com o provider real, nunca <c>smtp</c>.</item>
/// <item>SMTP 5xx é falha permanente (<see cref="ClassificadorDeFalha"/>); SMTP 4xx e rede são transitórios.</item>
/// </list>
/// </summary>
public sealed class SmtpEmailCanal(
    IEmailService emailService,
    ILogger<SmtpEmailCanal> logger) : ICanalNotificacao
{
    public CanalNotificacao Canal => CanalNotificacao.Email;

    public async Task<ResultadoEnvio> EnviarAsync(MensagemPronta mensagem, CancellationToken ct = default)
    {
        // O canal não chama o simulador: o console logaria o endereço, e o OutboxId já é o rastro (LGPD, #1292).
        if (emailService is IEmailServiceSimulado simulado)
        {
            logger.LogInformation(
                "Email simulado outbox={OutboxId} provider={Provider}: nada foi enviado",
                mensagem.OutboxId, simulado.Provider);

            return ResultadoEnvio.Simulado(simulado.Provider);
        }

        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            await emailService.SendAsync(
                mensagem.Destinatario,
                mensagem.Assunto,
                mensagem.Corpo,
                isHtml: true);

            sw.Stop();
            // Sem o endereço: dado pessoal fora do log (LGPD, #1292); o OutboxId leva à mensagem.
            logger.LogInformation(
                "Email enviado outbox={OutboxId} em {Ms}ms",
                mensagem.OutboxId, sw.ElapsedMilliseconds);

            return new ResultadoEnvio(
                Sucesso: true,
                ProviderUsado: "smtp",
                DuracaoMs: sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            logger.LogError(ex,
                "Falha ao enviar email outbox={OutboxId}",
                mensagem.OutboxId);

            return new ResultadoEnvio(
                Sucesso: false,
                ProviderUsado: "smtp",
                ErroDetalhado: ex.Message,
                DuracaoMs: sw.ElapsedMilliseconds,
                FalhaPermanente: ex is SmtpException smtp && ClassificadorDeFalha.SmtpEhPermanente(smtp));
        }
    }
}
