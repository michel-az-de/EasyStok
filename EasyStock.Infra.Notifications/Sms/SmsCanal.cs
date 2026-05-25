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
        logger.LogDebug(
            "Despachando SMS via provedor={Provedor} para={Destinatario}",
            provedor.Nome, mensagem.Destinatario);

        return await provedor.EnviarAsync(mensagem, ct);
    }
}
