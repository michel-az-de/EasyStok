namespace EasyStock.Application.Ports.Output.Atendimento;

/// <summary>
/// Cliente da Cloud API da Meta usado pelo agente de atendimento (S06) e pelo console (S07) para
/// mensagem rica — imagem, botões interativos, template e mídia recebida. O provider de
/// notificações (<c>MetaCloudWhatsAppProvider</c>) delega a este cliente em vez de falar HTTP
/// direto com a Meta.
/// </summary>
public interface IWhatsAppCloudClient
{
    Task<EnvioWhatsAppResult> EnviarTextoAsync(
        string waId, string texto, string? responderAWamid = null, CancellationToken ct = default);

    /// <summary><paramref name="urlPublica"/> precisa ser HTTPS pública — a Meta busca a imagem por lá.</summary>
    Task<EnvioWhatsAppResult> EnviarImagemAsync(
        string waId, string urlPublica, string? legenda = null, CancellationToken ct = default);

    /// <summary>No máximo 3 botões; título até 20 caracteres; id até 256. Lança <see cref="ArgumentException"/> antes de chamar a rede quando violado.</summary>
    Task<EnvioWhatsAppResult> EnviarBotoesAsync(
        string waId, string corpo, IReadOnlyList<(string Id, string Titulo)> botoes, CancellationToken ct = default);

    Task<EnvioWhatsAppResult> EnviarTemplateAsync(
        string waId,
        string nomeTemplate,
        string idioma,
        IReadOnlyList<string> parametrosCorpo,
        IReadOnlyList<(string Id, string Titulo)>? botoesQuickReply = null,
        CancellationToken ct = default);

    Task MarcarComoLidaAsync(string wamid, CancellationToken ct = default);

    Task<(Stream Conteudo, string MimeType)> BaixarMidiaAsync(string mediaId, CancellationToken ct = default);
}

public sealed record EnvioWhatsAppResult(string Wamid);

/// <summary>
/// Erro tipado da Cloud API, com o código numérico da Meta (ex.: 131047 = fora da janela de 24h).
/// <see cref="EhPermanente"/> = true nunca deve ser reenviado automaticamente.
/// </summary>
public sealed class WhatsAppCloudException(int codigo, string mensagem, bool ehPermanente)
    : Exception(mensagem)
{
    public int Codigo { get; } = codigo;
    public bool EhPermanente { get; } = ehPermanente;
}
