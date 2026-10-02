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
    /// <c>idioma</c>, <c>param1..paramN</c> e <c>botao1..botao3</c> para envio fora da janela de 24 h, e
    /// <c>imagem</c> (URL pública da arte da campanha, #1226) para o cabeçalho do template ou a imagem com legenda.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Metadados { get; init; }
}

/// <summary>
/// Desfecho tipado de um envio. Aditivo sobre <see cref="ResultadoEnvio"/>: quem usa o construtor antigo ganha o
/// desfecho derivado de <c>Sucesso</c> e <c>FalhaPermanente</c>. <see cref="Simulado"/> e o que o console e os stubs
/// devolvem: nada saiu, e quem le o resultado nao pode confundir com <see cref="Enviado"/>.
/// </summary>
public enum DesfechoEnvio
{
    Enviado,
    Simulado,
    FalhaTransitoria,
    FalhaPermanente,
}

public sealed record ResultadoEnvio(
    bool Sucesso,
    string? ProviderUsado = null,
    string? ErroDetalhado = null,
    int? StatusHttp = null,
    string? RespostaProviderJson = null,
    long DuracaoMs = 0,
    bool FalhaPermanente = false)
{
    public DesfechoEnvio Desfecho { get; init; } =
        Sucesso ? DesfechoEnvio.Enviado
        : FalhaPermanente ? DesfechoEnvio.FalhaPermanente
        : DesfechoEnvio.FalhaTransitoria;

    /// <summary>Nada saiu de verdade (console, stub): <c>Sucesso</c> segue verdadeiro para quem esta fora do motor.</summary>
    public static ResultadoEnvio Simulado(string provider, long duracaoMs = 0) =>
        new(Sucesso: true, ProviderUsado: provider, DuracaoMs: duracaoMs) { Desfecho = DesfechoEnvio.Simulado };
}

public interface ICanalNotificacao
{
    CanalNotificacao Canal { get; }
    Task<ResultadoEnvio> EnviarAsync(MensagemPronta mensagem, CancellationToken ct = default);
}
