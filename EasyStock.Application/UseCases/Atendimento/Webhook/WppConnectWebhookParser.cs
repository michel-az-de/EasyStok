using System.Text.Json;
using EasyStock.Application.Ports.Output.Atendimento;

namespace EasyStock.Application.UseCases.Atendimento.Webhook;

public static class WppConnectWebhookParser
{
    public static EventoWhatsApp Parse(string rawBody, string? telefoneResolvido = null, DateTimeOffset inicioDaCaptura = default)
    {
        using var doc = JsonDocument.Parse(rawBody);
        var root = doc.RootElement;
        var sessao = Texto(root, "session") ?? "";
        if (CredencialWppConnect.EmpresaDaSessao(sessao) is null)
            throw new FormatException("Sessão WPPConnect inválida.");
        var evento = Texto(root, "event");
        if (evento is not ("onmessage" or "unreadmessages" or "onselfmessage" or "onack")) return new([]);
        var id = Id(root);
        if (string.IsNullOrWhiteSpace(id)) throw new FormatException("Mensagem WPPConnect sem identificador.");
        var externo = $"wpp:{sessao}:{id}";

        if (evento == "onack")
        {
            var ack = root.TryGetProperty("ack", out var valor) && valor.TryGetInt32(out var numero) ? numero : 0;
            var status = ack switch { 1 => "sent", 2 => "delivered", 3 or 4 => "read", -1 => "failed", _ => null };
            return status is null ? new([]) : new([new(sessao, rawBody, [], [],
                [new(externo, status, null, ack == -1 ? "O WhatsApp recusou a mensagem." : null)], [])]);
        }

        var eco = evento == "onselfmessage" || (root.TryGetProperty("fromMe", out var deMim) && deMim.ValueKind == JsonValueKind.True);
        var contato = Texto(root, eco ? "to" : "from") ?? "";
        if (contato.EndsWith("@g.us", StringComparison.Ordinal) || contato.EndsWith("@broadcast", StringComparison.Ordinal)
            || (root.TryGetProperty("isGroupMsg", out var grupo) && grupo.ValueKind == JsonValueKind.True)) return new([]);
        var telefone = telefoneResolvido;
        if (string.IsNullOrWhiteSpace(telefone))
        {
            var endereco = contato;
            if (contato.EndsWith("@lid", StringComparison.Ordinal) && root.TryGetProperty("sender", out var sender))
                endereco = Texto(sender, "id") ?? contato;
            if (!endereco.EndsWith("@c.us", StringComparison.Ordinal))
                throw new FormatException("Contato sem telefone resolvido; o LID não é um número.");
            telefone = endereco[..^5];
        }
        if (telefone.Length is < 8 or > 15 || !telefone.All(char.IsAsciiDigit))
            throw new FormatException("Telefone WPPConnect inválido.");

        var tipo = Texto(root, "type") switch
        {
            "chat" => "text", "ptt" => "audio",
            "image" => "image", "audio" => "audio", "video" => "video",
            "document" => "document", "sticker" => "sticker", "location" => "location",
            "reaction" => "reaction", _ => "unsupported"
        };
        var midia = tipo is "image" or "audio" or "video" or "document" or "sticker" ? $"wpp:{id}" : null;
        var texto = tipo == "text" ? Texto(root, "body") : Texto(root, "caption");
        if (!root.TryGetProperty("t", out var t) || !t.TryGetInt64(out var segundos))
            throw new FormatException("Mensagem WPPConnect sem data.");
        var quando = DateTimeOffset.FromUnixTimeSeconds(segundos);
        if (!eco && inicioDaCaptura != default && quando < inicioDaCaptura) return new([]);
        var nome = root.TryGetProperty("sender", out var remetente) ? Texto(remetente, "pushname") : null;
        var contatos = new ContatoWhatsApp[] { new(telefone, nome) };
        return new([new(sessao, rawBody, contatos,
            eco ? [] : [new(telefone, externo, quando, tipo, texto, midia, Texto(root, "mimetype"), null, null)],
            [], eco ? [new(telefone, externo, quando, tipo, texto, midia, Texto(root, "mimetype"))] : [])]);
    }

    public static string? Texto(JsonElement root, string nome) =>
        root.ValueKind == JsonValueKind.Object && root.TryGetProperty(nome, out var valor) && valor.ValueKind == JsonValueKind.String
            ? valor.GetString() : null;

    public static string? Id(JsonElement root)
    {
        if (!root.TryGetProperty("id", out var id)) return null;
        return id.ValueKind == JsonValueKind.String ? id.GetString() : Texto(id, "_serialized");
    }
}
