using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Domain.Enums.Atendimento;

namespace EasyStock.Infra.Integrations.Meta;

/// <summary>
/// Adaptador comum do Messenger e do Instagram na porta de canal (S35). Dentro da janela vai
/// <c>messaging_type=RESPONSE</c>; fora dela, só com tag (<see cref="ICanalComTagHumana"/>), usada pelo
/// console. Modelo não existe nestes canais.
/// </summary>
public abstract class CanalMetaMensageria(IMetaMensageriaTransporte transporte) : ICanalMensageria, ICanalComTagHumana
{
    public const int MaximoRespostasRapidas = 13;
    public const int TituloRespostaRapidaMaximo = 20;

    public abstract CanalConversa Canal { get; }

    public Task<string> EnviarTextoAsync(string contatoIdExterno, string texto, CancellationToken ct = default) =>
        transporte.EnviarAsync(new
        {
            recipient = new { id = contatoIdExterno },
            messaging_type = "RESPONSE",
            message = new { text = texto },
        }, ct);

    public Task<string> EnviarTextoComTagAsync(string contatoIdExterno, string texto, string tag, CancellationToken ct = default) =>
        transporte.EnviarAsync(new
        {
            recipient = new { id = contatoIdExterno },
            messaging_type = "MESSAGE_TAG",
            tag,
            message = new { text = texto },
        }, ct);

    public async Task<string> EnviarImagemAsync(string contatoIdExterno, string urlPublica, string? legenda = null, CancellationToken ct = default)
    {
        // A Send API não tem legenda no anexo: a legenda sai como texto logo antes da imagem.
        if (!string.IsNullOrWhiteSpace(legenda))
            await EnviarTextoAsync(contatoIdExterno, legenda, ct);
        return await transporte.EnviarAsync(new
        {
            recipient = new { id = contatoIdExterno },
            messaging_type = "RESPONSE",
            message = new { attachment = new { type = "image", payload = new { url = urlPublica, is_reusable = false } } },
        }, ct);
    }

    public virtual Task<string> EnviarBotoesAsync(
        string contatoIdExterno, string corpo, IReadOnlyList<(string Id, string Titulo)> botoes, CancellationToken ct = default) =>
        throw new NotSupportedException($"O canal {Canal} não aceita botões.");

    public Task<string> EnviarModeloAsync(
        string contatoIdExterno, string nome, string idioma, IReadOnlyList<string> parametros, CancellationToken ct = default) =>
        throw new NotSupportedException($"O canal {Canal} não tem modelo: fora da janela só vale resposta humana com tag.");

    /// <summary>A Meta marca leitura por remetente (<c>sender_action</c>), não por mensagem: nada a fazer com o id.</summary>
    public Task MarcarComoLidaAsync(string idMensagemExterna, CancellationToken ct = default) => Task.CompletedTask;

    protected Task<string> EnviarRespostasRapidasAsync(
        string contatoIdExterno, string corpo, IReadOnlyList<(string Id, string Titulo)> botoes, CancellationToken ct)
    {
        if (botoes.Count is 0 or > MaximoRespostasRapidas)
            throw new ArgumentException($"Respostas rápidas aceitam de 1 a {MaximoRespostasRapidas} opções.", nameof(botoes));
        foreach (var (_, titulo) in botoes)
        {
            if (titulo.Length > TituloRespostaRapidaMaximo)
                throw new ArgumentException($"Título de resposta rápida excede {TituloRespostaRapidaMaximo} caracteres: \"{titulo}\".", nameof(botoes));
        }

        return transporte.EnviarAsync(new
        {
            recipient = new { id = contatoIdExterno },
            messaging_type = "RESPONSE",
            message = new
            {
                text = corpo,
                quick_replies = botoes.Select(b => new { content_type = "text", title = b.Titulo, payload = b.Id }).ToArray(),
            },
        }, ct);
    }
}

/// <summary>Messenger (página do Facebook): aceita respostas rápidas como botões.</summary>
public sealed class CanalMessenger(IMetaMensageriaTransporte transporte) : CanalMetaMensageria(transporte)
{
    public override CanalConversa Canal => CanalConversa.Messenger;

    public override Task<string> EnviarBotoesAsync(
        string contatoIdExterno, string corpo, IReadOnlyList<(string Id, string Titulo)> botoes, CancellationToken ct = default) =>
        EnviarRespostasRapidasAsync(contatoIdExterno, corpo, botoes, ct);
}

/// <summary>Instagram Direct (conta profissional ligada à página): sem botões.</summary>
public sealed class CanalInstagram(IMetaMensageriaTransporte transporte) : CanalMetaMensageria(transporte)
{
    public override CanalConversa Canal => CanalConversa.Instagram;
}