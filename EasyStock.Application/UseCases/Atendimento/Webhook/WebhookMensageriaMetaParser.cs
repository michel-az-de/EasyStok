using System.Text.Json;
using EasyStock.Domain.Enums.Atendimento;

namespace EasyStock.Application.UseCases.Atendimento.Webhook;

/// <summary>Payload do webhook da Meta para Instagram e Messenger já normalizado (S35).</summary>
public sealed record EventoMensageriaMeta(IReadOnlyList<MensagemRecebidaMeta> Mensagens);

/// <summary>
/// Uma entrada de <c>entry[].messaging[]</c>. <see cref="SenderId"/> é o IGSID ou o PSID do cliente;
/// <see cref="RecipientId"/> é a conta do Instagram ou a página, que roteia o tenant. <see cref="Eco"/>
/// marca a cópia de uma mensagem que nós mesmos enviamos.
/// </summary>
public sealed record MensagemRecebidaMeta(
    CanalConversa Canal,
    string RecipientId,
    string SenderId,
    string Mid,
    DateTimeOffset Timestamp,
    string? Texto,
    string? TipoAnexo,
    string? PostbackPayload,
    bool Eco);

/// <summary>
/// Parser tolerante (como o <see cref="WebhookWhatsAppParser"/>): campo ausente ou de tipo inesperado
/// vira nulo; evento sem <c>mid</c> (leitura, entrega) é ignorado; <c>object</c> desconhecido não
/// devolve nada.
/// </summary>
public static class WebhookMensageriaMetaParser
{
    public static EventoMensageriaMeta Parse(string rawBody)
    {
        using var doc = JsonDocument.Parse(rawBody);
        var raiz = doc.RootElement;
        var canal = Texto(raiz, "object") switch
        {
            "instagram" => CanalConversa.Instagram,
            "page" => CanalConversa.Messenger,
            _ => (CanalConversa?)null,
        };

        var mensagens = new List<MensagemRecebidaMeta>();
        if (canal is null || !raiz.TryGetProperty("entry", out var entries) || entries.ValueKind != JsonValueKind.Array)
            return new EventoMensageriaMeta(mensagens);

        foreach (var entry in entries.EnumerateArray())
        {
            if (!entry.TryGetProperty("messaging", out var eventos) || eventos.ValueKind != JsonValueKind.Array)
                continue;

            foreach (var evento in eventos.EnumerateArray())
            {
                if (Ler(canal.Value, evento) is { } mensagem)
                    mensagens.Add(mensagem);
            }
        }

        return new EventoMensageriaMeta(mensagens);
    }

    private static MensagemRecebidaMeta? Ler(CanalConversa canal, JsonElement evento)
    {
        var sender = Objeto(evento, "sender") is { } s ? Texto(s, "id") : null;
        var recipient = Objeto(evento, "recipient") is { } r ? Texto(r, "id") : null;
        if (string.IsNullOrEmpty(sender) || string.IsNullOrEmpty(recipient))
            return null;

        var timestamp = evento.TryGetProperty("timestamp", out var ts) && ts.ValueKind == JsonValueKind.Number
            ? DateTimeOffset.FromUnixTimeMilliseconds(ts.GetInt64())
            : DateTimeOffset.UtcNow;

        if (Objeto(evento, "message") is { } message)
        {
            var mid = Texto(message, "mid");
            if (string.IsNullOrEmpty(mid)) return null;

            string? tipoAnexo = null;
            if (message.TryGetProperty("attachments", out var anexos) && anexos.ValueKind == JsonValueKind.Array)
                tipoAnexo = anexos.EnumerateArray().Select(a => Texto(a, "type")).FirstOrDefault(t => t is not null);

            var eco = message.TryGetProperty("is_echo", out var e) && e.ValueKind == JsonValueKind.True;
            return new MensagemRecebidaMeta(canal, recipient, sender, mid, timestamp, Texto(message, "text"), tipoAnexo, null, eco);
        }

        if (Objeto(evento, "postback") is { } postback)
        {
            var mid = Texto(postback, "mid");
            if (string.IsNullOrEmpty(mid)) return null;
            return new MensagemRecebidaMeta(canal, recipient, sender, mid, timestamp,
                Texto(postback, "title"), null, Texto(postback, "payload"), Eco: false);
        }

        return null;
    }

    private static JsonElement? Objeto(JsonElement e, string nome) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(nome, out var v) && v.ValueKind == JsonValueKind.Object ? v : null;

    private static string? Texto(JsonElement e, string nome) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(nome, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
