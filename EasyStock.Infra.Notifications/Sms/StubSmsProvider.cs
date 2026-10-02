using EasyStock.Application.Ports.Output.Notifications;
using Microsoft.Extensions.Logging;

namespace EasyStock.Infra.Notifications.Sms;

/// <summary>
/// Provedor SMS stub para desenvolvimento/testes — não envia nada. Devolve <see cref="DesfechoEnvio.Simulado"/>
/// com provider <c>stub</c>: o outbox fica <c>Simulado</c>, nunca <c>Enviado</c>. O log leva só o
/// <c>OutboxId</c>, sem telefone nem corpo (LGPD, #1292).
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
            logger.LogWarning("[STUB-SMS] Falha simulada outbox={OutboxId}", mensagem.OutboxId);
            return Task.FromResult(new ResultadoEnvio(Sucesso: false, ProviderUsado: "stub",
                ErroDetalhado: "Falha simulada"));
        }

        MensagensEnviadas.Add(mensagem);
        logger.LogInformation("[STUB-SMS] simulado, nada foi enviado outbox={OutboxId}", mensagem.OutboxId);

        return Task.FromResult(ResultadoEnvio.Simulado("stub", duracaoMs: 1));
    }
}
