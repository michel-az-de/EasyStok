using EasyStock.Domain.Enums.Notifications;

namespace EasyStock.Application.Ports.Output.Notifications;

public sealed record MensagemPronta(
    Guid OutboxId,
    Guid EmpresaId,
    string Destinatario,
    string Assunto,
    string Corpo,
    CanalNotificacao Canal,
    CategoriaConteudoNotificacao Categoria,
    string? ProviderOverride = null);

public sealed record ResultadoEnvio(
    bool Sucesso,
    string? ProviderUsado = null,
    string? ErroDetalhado = null,
    int? StatusHttp = null,
    string? RespostaProviderJson = null,
    long DuracaoMs = 0);

public interface ICanalNotificacao
{
    CanalNotificacao Canal { get; }
    Task<ResultadoEnvio> EnviarAsync(MensagemPronta mensagem, CancellationToken ct = default);
}
