using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Domain.Enums.Notifications;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EasyStock.Infra.Notifications.WhatsApp;

/// <summary>
/// Canal de WhatsApp do outbox. O remetente é escolhido por mensagem (N6): sem <see cref="MensagemPronta.ProviderOverride"/>
/// vale o provider ativo da loja (<c>whatsapp:active</c>); com ele, o provider de chave <c>whatsapp:{override}</c>
/// (<c>plataforma</c> é o 2º número da WABA). Override sem provider registrado é falha permanente
/// <see cref="ErroProviderNaoConfigurado"/>, sem chamar a Meta: a plataforma nunca cai no número da loja.
/// </summary>
public sealed class WhatsAppCanal(
    [FromKeyedServices("whatsapp:active")] IProvedorWhatsApp provedor,
    IServiceProvider services,
    ILogger<WhatsAppCanal> logger) : ICanalNotificacao
{
    public const string ErroProviderNaoConfigurado = "provider_nao_configurado";

    public CanalNotificacao Canal => CanalNotificacao.WhatsApp;

    public async Task<ResultadoEnvio> EnviarAsync(MensagemPronta mensagem, CancellationToken ct = default)
    {
        var escolhido = Escolher(mensagem.ProviderOverride);
        if (escolhido is null)
        {
            logger.LogError(
                "WhatsApp: override sem provider configurado, mensagem recusada sem chamar a Meta override={Override} outbox={OutboxId}",
                mensagem.ProviderOverride, mensagem.OutboxId);
            return new ResultadoEnvio(Sucesso: false, ProviderUsado: $"whatsapp:{mensagem.ProviderOverride}",
                ErroDetalhado: ErroProviderNaoConfigurado, FalhaPermanente: true);
        }

        // Sem o telefone: dado pessoal fora do log (LGPD, #1292); o OutboxId leva à mensagem.
        logger.LogDebug(
            "Despachando WhatsApp via provedor={Provedor} outbox={OutboxId}",
            escolhido.Nome, mensagem.OutboxId);

        return await escolhido.EnviarAsync(mensagem, ct);
    }

    private IProvedorWhatsApp? Escolher(string? providerOverride)
    {
        if (string.IsNullOrWhiteSpace(providerOverride))
            return provedor;

        // Só a chave conhecida vira nome de serviço; o resto é recusado sem consultar o contêiner.
        return providerOverride == MensagemPronta.ProviderOverridePlataforma
            ? services.GetKeyedService<IProvedorWhatsApp>($"whatsapp:{providerOverride}")
            : null;
    }
}
