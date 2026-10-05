using System.Text.Json;

namespace EasyStock.Application.UseCases.Atendimento.Webhook;

/// <summary>
/// Parser tolerante do payload do webhook da Meta: campo ausente ou de tipo inesperado vira
/// default/null em vez de lançar — a Meta pode adicionar campos novos a qualquer momento.
/// </summary>
public static class WebhookWhatsAppParser
{
    public static EventoWhatsApp Parse(string rawBody)
    {
        using var doc = JsonDocument.Parse(rawBody);
        var entradas = new List<EntradaWhatsApp>();

        if (doc.RootElement.TryGetProperty("entry", out var entryArr) && entryArr.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in entryArr.EnumerateArray())
            {
                if (!entry.TryGetProperty("changes", out var changesArr) || changesArr.ValueKind != JsonValueKind.Array)
                    continue;

                foreach (var change in changesArr.EnumerateArray())
                {
                    if (change.TryGetProperty("value", out var value))
                        entradas.Add(ParseValue(value));
                }
            }
        }

        return new EventoWhatsApp(entradas);
    }

    private static EntradaWhatsApp ParseValue(JsonElement value)
    {
        var phoneNumberId = value.TryGetProperty("metadata", out var meta)
            ? GetString(meta, "phone_number_id") ?? ""
            : "";

        var contatos = new List<ContatoWhatsApp>();
        if (value.TryGetProperty("contacts", out var contactsArr) && contactsArr.ValueKind == JsonValueKind.Array)
        {
            foreach (var c in contactsArr.EnumerateArray())
            {
                var waId = GetString(c, "wa_id") ?? "";
                string? nome = c.TryGetProperty("profile", out var profile) ? GetString(profile, "name") : null;
                contatos.Add(new ContatoWhatsApp(waId, nome));
            }
        }

        var mensagens = new List<MensagemRecebidaWhatsApp>();
        if (value.TryGetProperty("messages", out var msgsArr) && msgsArr.ValueKind == JsonValueKind.Array)
        {
            foreach (var m in msgsArr.EnumerateArray())
                mensagens.Add(ParseMensagem(m));
        }

        var statuses = new List<StatusRecebidoWhatsApp>();
        if (value.TryGetProperty("statuses", out var statusesArr) && statusesArr.ValueKind == JsonValueKind.Array)
        {
            foreach (var s in statusesArr.EnumerateArray())
                statuses.Add(ParseStatus(s));
        }

        return new EntradaWhatsApp(phoneNumberId, value.GetRawText(), contatos, mensagens, statuses);
    }

    private static MensagemRecebidaWhatsApp ParseMensagem(JsonElement m)
    {
        var de = GetString(m, "from") ?? "";
        var wamid = GetString(m, "id") ?? "";
        var tipo = GetString(m, "type") ?? "unsupported";

        var timestamp = long.TryParse(GetString(m, "timestamp"), out var epochSeconds) && epochSeconds > 0
            ? DateTimeOffset.FromUnixTimeSeconds(epochSeconds)
            : DateTimeOffset.UtcNow;

        string? texto = null, midiaId = null, midiaMime = null, botaoRespostaId = null, botaoPayload = null;

        switch (tipo)
        {
            case "text":
                if (m.TryGetProperty("text", out var t)) texto = GetString(t, "body");
                break;
            case "image" or "audio" or "document" or "sticker" or "video":
                if (m.TryGetProperty(tipo, out var media))
                {
                    midiaId = GetString(media, "id");
                    midiaMime = GetString(media, "mime_type");
                    // Legenda de imagem, documento e vídeo: o agente e o console leem junto do marcador.
                    texto = GetString(media, "caption");
                }
                break;
            case "location":
                if (m.TryGetProperty("location", out var local))
                    texto = TextoDaLocalizacao(local);
                break;
            case "interactive":
                if (m.TryGetProperty("interactive", out var interactive)
                    && interactive.TryGetProperty("button_reply", out var buttonReply))
                {
                    botaoRespostaId = GetString(buttonReply, "id");
                }
                break;
            case "button":
                if (m.TryGetProperty("button", out var btn))
                    botaoPayload = GetString(btn, "payload");
                break;
        }

        return new MensagemRecebidaWhatsApp(de, wamid, timestamp, tipo, texto, midiaId, midiaMime, botaoRespostaId, botaoPayload);
    }

    private static StatusRecebidoWhatsApp ParseStatus(JsonElement s)
    {
        var wamid = GetString(s, "id") ?? "";
        var status = GetString(s, "status") ?? "";
        var recipientId = GetString(s, "recipient_id");

        string? erro = null;
        int? codigo = null;
        if (s.TryGetProperty("errors", out var errorsArr) && errorsArr.ValueKind == JsonValueKind.Array && errorsArr.GetArrayLength() > 0)
        {
            var primeiro = errorsArr[0];
            erro = GetString(primeiro, "title") ?? GetString(primeiro, "message");
            if (primeiro.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.Number && c.TryGetInt32(out var n))
                codigo = n;
        }

        return new StatusRecebidoWhatsApp(wamid, status, recipientId, erro, codigo);
    }

    /// <summary>"Nome; endereço; lat,long", só com as partes presentes. Coordenadas no texto bruto da Meta.</summary>
    private static string? TextoDaLocalizacao(JsonElement local)
    {
        var latitude = Numero(local, "latitude");
        var longitude = Numero(local, "longitude");
        var partes = new[]
        {
            GetString(local, "name"),
            GetString(local, "address"),
            latitude is not null && longitude is not null ? $"{latitude},{longitude}" : null,
        }.Where(p => !string.IsNullOrWhiteSpace(p)).ToList();

        return partes.Count == 0 ? null : string.Join("; ", partes);
    }

    private static string? Numero(JsonElement element, string propriedade) =>
        element.TryGetProperty(propriedade, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetRawText() : null;

    private static string? GetString(JsonElement element, string propriedade) =>
        element.TryGetProperty(propriedade, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
