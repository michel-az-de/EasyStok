using EasyStock.Application.Ports.Output.Notifications;
using Microsoft.Extensions.Logging;

namespace EasyStock.Infra.Notifications.Sms;

/// <summary>
/// Provedor SMS stub para desenvolvimento/testes — loga a mensagem sem enviar.
/// </summary>
public sealed class StubSmsProvider(ILogger<StubSmsProvider> logger) : IProvedorSms
{
    public string Nome => "stub";

    // Para testes: mensagens capturadas podem ser inspecionadas
    public List<MensagemPronta> MensagensEnviadas { get; } = [];
    public bool SimularFalha { get; set; }

    public Task<ResultadoEnvio> EnviarAsync(MensagemPronta mensagem, CancellationToken ct = default)
    {
        if (SimularFalha)
        {
            logger.LogWarning("[STUB-SMS] Falha simulada para {Destinatario}", mensagem.Destinatario);
            return Task.FromResult(new ResultadoEnvio(Sucesso: false, ProviderUsado: "stub",
                ErroDetalhado: "Falha simulada"));
        }

        MensagensEnviadas.Add(mensagem);
        logger.LogInformation(
            "[STUB-SMS] → {Destinatario} | Assunto: {Assunto}", mensagem.Destinatario, mensagem.Assunto);

        return Task.FromResult(new ResultadoEnvio(Sucesso: true, ProviderUsado: "stub", DuracaoMs: 1));
    }
}
