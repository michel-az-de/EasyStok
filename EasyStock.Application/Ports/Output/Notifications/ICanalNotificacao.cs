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
    /// <summary>Chave do <see cref="ProviderOverride"/> do WhatsApp de plataforma (N6): <c>whatsapp:plataforma</c>. Loja não leva override.</summary>
    public const string ProviderOverridePlataforma = "plataforma";

    /// <summary>
    /// Dados do envio que o canal interpreta (S09). No WhatsApp da Meta: <c>template</c>,
    /// <c>idioma</c>, <c>param1..paramN</c> e <c>botao1..botao3</c> para envio fora da janela de 24 h, e
    /// <c>imagem</c> (URL pública da arte da campanha, #1226) para o cabeçalho do template ou a imagem com legenda.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Metadados { get; init; }
}

/// <summary>
/// Como o envio terminou, na visão do canal (N2). O dispatcher traduz cada valor num status do outbox; o canal
/// nunca grava status.
/// </summary>
public enum DesfechoEnvio
{
    /// <summary>O provider aceitou a mensagem.</summary>
    Enviado = 1,

    /// <summary>
    /// Stub ou console: nada saiu e o canal disse isso. Para quem está fora do motor, o resultado segue como
    /// <see cref="ResultadoEnvio.Sucesso"/>; só o dispatcher grava <c>StatusOutbox.Simulado</c>.
    /// </summary>
    Simulado = 2,

    /// <summary>
    /// Erro que pode passar (HTTP 5xx, 408 e 429, rede, SMTP 4xx) em canal que repete sem risco (e-mail, push,
    /// in-app): o outbox tenta de novo com backoff.
    /// </summary>
    FalhaTransitoria = 3,

    /// <summary>
    /// Erro que nunca vai passar (HTTP 4xx exceto 408 e 429, SMTP 5xx): sem nova tentativa, e o fallback de canal
    /// assume.
    /// </summary>
    FalhaPermanente = 4,

    /// <summary>
    /// Pode ter saído ou não (timeout, queda de conexão ou 5xx no WhatsApp e no SMS, que saem no máximo uma vez):
    /// terminal, sem reenvio e sem fallback de canal, para não duplicar.
    /// </summary>
    Indeterminado = 5
}

/// <summary>
/// Resultado de uma chamada de canal. O construtor posicional continua valendo para quem só conhece
/// <see cref="Sucesso"/> e <see cref="FalhaPermanente"/>; o <see cref="Desfecho"/> nasce deles.
/// </summary>
public sealed record ResultadoEnvio(
    bool Sucesso,
    string? ProviderUsado = null,
    string? ErroDetalhado = null,
    int? StatusHttp = null,
    string? RespostaProviderJson = null,
    long DuracaoMs = 0,
    bool FalhaPermanente = false)
{
    /// <summary>
    /// Desfecho tipado. Sem fixar, deriva de <see cref="Sucesso"/> e <see cref="FalhaPermanente"/>; as fábricas
    /// <see cref="Simulado"/> e <see cref="Indeterminado"/> o fixam.
    /// </summary>
    public DesfechoEnvio Desfecho { get; init; } = Sucesso
        ? DesfechoEnvio.Enviado
        : FalhaPermanente ? DesfechoEnvio.FalhaPermanente : DesfechoEnvio.FalhaTransitoria;

    /// <summary>
    /// Id da mensagem no provider (o <c>wamid</c> da Meta), quando ele devolve um. A N1 grava em
    /// <c>ProviderMensagemId</c> e a N6 usa no webhook de status.
    /// </summary>
    public string? IdExterno { get; init; }

    /// <summary>Nada saiu e o canal disse isso (stub, console). <see cref="Sucesso"/> segue verdadeiro para quem está fora do motor.</summary>
    public static ResultadoEnvio Simulado(string provider, long duracaoMs = 0) =>
        new(Sucesso: true, ProviderUsado: provider, DuracaoMs: duracaoMs) { Desfecho = DesfechoEnvio.Simulado };

    /// <summary>Pode ter saído ou não. Nunca é sucesso nem falha permanente: reenviar duplicaria, descartar perderia.</summary>
    public static ResultadoEnvio Indeterminado(string provider, string erro, int? statusHttp = null, long duracaoMs = 0) =>
        new(Sucesso: false, ProviderUsado: provider, ErroDetalhado: erro, StatusHttp: statusHttp, DuracaoMs: duracaoMs)
        {
            Desfecho = DesfechoEnvio.Indeterminado
        };
}

public interface ICanalNotificacao
{
    CanalNotificacao Canal { get; }
    Task<ResultadoEnvio> EnviarAsync(MensagemPronta mensagem, CancellationToken ct = default);
}
