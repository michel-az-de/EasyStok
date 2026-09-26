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
    string? ProviderOverride = null)
{
    /// <summary>
    /// Dados do envio que o canal interpreta (S09). No WhatsApp da Meta: <c>template</c>,
    /// <c>idioma</c>, <c>param1..paramN</c> e <c>botao1..botao3</c> para envio fora da janela de 24 h.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Metadados { get; init; }
}

public sealed record ResultadoEnvio(
    bool Sucesso,
    string? ProviderUsado = null,
    string? ErroDetalhado = null,
    int? StatusHttp = null,
    string? RespostaProviderJson = null,
    long DuracaoMs = 0,
    bool FalhaPermanente = false);

public interface ICanalNotificacao
{
    CanalNotificacao Canal { get; }
    Task<ResultadoEnvio> EnviarAsync(MensagemPronta mensagem, CancellationToken ct = default);
}
