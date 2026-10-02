using EasyStock.Domain.Enums.Notifications;

namespace EasyStock.Domain.Entities.Notifications;

public class LogEnvioNotificacao
{
    public Guid Id { get; set; }
    public Guid OutboxMensagemId { get; set; }
    public int Tentativa { get; set; }
    public CanalNotificacao Canal { get; set; }
    public string Provider { get; set; } = null!;
    public int? StatusHttp { get; set; }
    public string? RespostaProviderJson { get; set; }
    public long DuracaoMs { get; set; }
    public DateTime OcorridoEm { get; set; }
    public string? ErroDetalhado { get; set; }
    public bool BypassConsentimento { get; set; }
    public bool Sucesso { get; set; }

    public OutboxMensagemNotificacao? OutboxMensagem { get; set; }

    /// <summary><see cref="ErroDetalhado"/> de um envio simulado (stub ou console): o log não ganhou coluna nova.</summary>
    public const string ErroSimulado = "simulado";

    /// <summary>
    /// Envio simulado (N2): nada saiu. O log guarda o provider real (<c>stub</c>, <c>console</c>) com
    /// <see cref="Sucesso"/> falso e <see cref="ErroDetalhado"/> igual a <see cref="ErroSimulado"/>, para a
    /// auditoria não contar como entregue o que não saiu.
    /// </summary>
    public static LogEnvioNotificacao RegistrarSimulado(
        Guid outboxMensagemId,
        int tentativa,
        CanalNotificacao canal,
        string provider,
        long duracaoMs,
        bool bypassConsentimento = false)
    {
        var log = RegistrarFalha(outboxMensagemId, tentativa, canal, provider, duracaoMs, ErroSimulado);
        log.BypassConsentimento = bypassConsentimento;
        return log;
    }

    public static LogEnvioNotificacao RegistrarSucesso(
        Guid outboxMensagemId,
        int tentativa,
        CanalNotificacao canal,
        string provider,
        long duracaoMs,
        int? statusHttp = null,
        string? respostaProviderJson = null,
        bool bypassConsentimento = false) => new()
    {
        Id = Guid.NewGuid(),
        OutboxMensagemId = outboxMensagemId,
        Tentativa = tentativa,
        Canal = canal,
        Provider = provider,
        StatusHttp = statusHttp,
        RespostaProviderJson = respostaProviderJson,
        DuracaoMs = duracaoMs,
        OcorridoEm = DateTime.UtcNow,
        Sucesso = true,
        BypassConsentimento = bypassConsentimento
    };

    public static LogEnvioNotificacao RegistrarFalha(
        Guid outboxMensagemId,
        int tentativa,
        CanalNotificacao canal,
        string provider,
        long duracaoMs,
        string erroDetalhado,
        int? statusHttp = null,
        string? respostaProviderJson = null) => new()
    {
        Id = Guid.NewGuid(),
        OutboxMensagemId = outboxMensagemId,
        Tentativa = tentativa,
        Canal = canal,
        Provider = provider,
        StatusHttp = statusHttp,
        RespostaProviderJson = respostaProviderJson,
        DuracaoMs = duracaoMs,
        OcorridoEm = DateTime.UtcNow,
        Sucesso = false,
        ErroDetalhado = erroDetalhado
    };
}
