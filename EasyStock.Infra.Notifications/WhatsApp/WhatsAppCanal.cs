using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Domain.Enums.Notifications;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EasyStock.Infra.Notifications.WhatsApp;

public sealed class WhatsAppCanal(
    [FromKeyedServices("whatsapp:active")] IProvedorWhatsApp provedor,
    ILogger<WhatsAppCanal> logger) : ICanalNotificacao
{
    public CanalNotificacao Canal => CanalNotificacao.WhatsApp;

    public async Task<ResultadoEnvio> EnviarAsync(MensagemPronta mensagem, CancellationToken ct = default)
    {
        logger.LogDebug(
            "Despachando WhatsApp via provedor={Provedor} para={Destinatario}",
            provedor.Nome, mensagem.Destinatario);

        return await provedor.EnviarAsync(mensagem, ct);
    }
}
