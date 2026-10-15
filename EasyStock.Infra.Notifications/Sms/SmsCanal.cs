using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Domain.Enums.Notifications;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EasyStock.Infra.Notifications.Sms;

public sealed class SmsCanal(
    [FromKeyedServices("sms:active")] IProvedorSms provedor,
    ILogger<SmsCanal> logger) : ICanalNotificacao
{
    public CanalNotificacao Canal => CanalNotificacao.Sms;

    public async Task<ResultadoEnvio> EnviarAsync(MensagemPronta mensagem, CancellationToken ct = default)
    {
        // Sem o telefone: dado pessoal fora do log (LGPD, #1292); o OutboxId leva à mensagem.
        logger.LogDebug(
            "Despachando SMS via provedor={Provedor} outbox={OutboxId}",
            provedor.Nome, mensagem.OutboxId);

        return await provedor.EnviarAsync(mensagem, ct);
    }
}
