using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Domain.Enums.Atendimento;

namespace EasyStock.Infra.Integrations.WhatsApp;

/// <summary>
/// Adaptador do WhatsApp quando a ponte não oficial está ligada (#2072, ADR-0062): o mesmo <see cref="CanalWhatsApp"/>,
/// marcado como <see cref="ICanalSemModeloAprovado"/> para o outbox mandar o texto onde a Meta mandaria o modelo.
/// </summary>
public sealed class CanalWhatsAppPonte(IWhatsAppCloudClient cloud)
    : ICanalMensageria, ICanalComAudio, ICanalSemModeloAprovado, ICanalComAcoesNaMensagem
{
    private readonly CanalWhatsApp _canal = new(cloud);

    public Task ReagirAsync(string contatoIdExterno, string idMensagemExterna, string? emoji, CancellationToken ct = default) =>
        _canal.ReagirAsync(contatoIdExterno, idMensagemExterna, emoji, ct);

    public Task ApagarParaTodosAsync(string contatoIdExterno, string idMensagemExterna, CancellationToken ct = default) =>
        _canal.ApagarParaTodosAsync(contatoIdExterno, idMensagemExterna, ct);

    public CanalConversa Canal => _canal.Canal;

    public Task<string> EnviarTextoAsync(string contatoIdExterno, string texto, CancellationToken ct = default) =>
        _canal.EnviarTextoAsync(contatoIdExterno, texto, ct);

    public Task<string> EnviarImagemAsync(string contatoIdExterno, string urlPublica, string? legenda = null, CancellationToken ct = default) =>
        _canal.EnviarImagemAsync(contatoIdExterno, urlPublica, legenda, ct);

    public Task<string> EnviarAudioAsync(string contatoIdExterno, string urlPublica, bool notaDeVoz, CancellationToken ct = default) =>
        _canal.EnviarAudioAsync(contatoIdExterno, urlPublica, notaDeVoz, ct);

    public Task<string> EnviarBotoesAsync(
        string contatoIdExterno, string corpo, IReadOnlyList<(string Id, string Titulo)> botoes, CancellationToken ct = default) =>
        _canal.EnviarBotoesAsync(contatoIdExterno, corpo, botoes, ct);

    public Task<string> EnviarModeloAsync(
        string contatoIdExterno, string nome, string idioma, IReadOnlyList<string> parametros, CancellationToken ct = default) =>
        _canal.EnviarModeloAsync(contatoIdExterno, nome, idioma, parametros, ct);

    public Task<string> EnviarModeloAsync(
        string contatoIdExterno, string nome, string idioma, IReadOnlyList<string> parametros,
        IReadOnlyList<(string Id, string Titulo)>? botoes, CancellationToken ct = default) =>
        _canal.EnviarModeloAsync(contatoIdExterno, nome, idioma, parametros, botoes, ct);

    public Task<string> EnviarModeloComImagemAsync(
        string contatoIdExterno, string nome, string idioma, IReadOnlyList<string> parametros, string urlImagem,
        CancellationToken ct = default) =>
        _canal.EnviarModeloComImagemAsync(contatoIdExterno, nome, idioma, parametros, urlImagem, ct);

    public Task MarcarComoLidaAsync(string idMensagemExterna, CancellationToken ct = default) =>
        _canal.MarcarComoLidaAsync(idMensagemExterna, ct);
}
