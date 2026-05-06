using EasyStock.Application.Ports.Output.Notifications;
using Microsoft.Extensions.Logging;

namespace EasyStock.Infra.Notifications.WhatsApp;

public sealed class StubWhatsAppProvider(ILogger<StubWhatsAppProvider> logger) : IProvedorWhatsApp
{
    public string Nome => "stub";

    public List<MensagemPronta> MensagensEnviadas { get; } = [];
    public bool SimularFalha { get; set; }

    public Task<ResultadoEnvio> EnviarAsync(MensagemPronta mensagem, CancellationToken ct = default)
    {
        if (SimularFalha)
        {
            logger.LogWarning("[STUB-WA] Falha simulada para {Destinatario}", mensagem.Destinatario);
            return Task.FromResult(new ResultadoEnvio(Sucesso: false, ProviderUsado: "stub",
                ErroDetalhado: "Falha simulada"));
        }

        MensagensEnviadas.Add(mensagem);
        logger.LogInformation(
            "[STUB-WA] → {Destinatario} | {Corpo}", mensagem.Destinatario, mensagem.Corpo[..Math.Min(50, mensagem.Corpo.Length)]);

        return Task.FromResult(new ResultadoEnvio(Sucesso: true, ProviderUsado: "stub", DuracaoMs: 1));
    }
}
