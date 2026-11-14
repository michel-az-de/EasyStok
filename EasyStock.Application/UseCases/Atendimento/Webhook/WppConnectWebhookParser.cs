using System.Globalization;
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
        if (evento != "onack" && Grupo(root)) return new([]);
        var id = Id(root);
        // #2590: só os nomes das chaves vão para o log, nunca os valores (telefone e texto do cliente).
        if (string.IsNullOrWhiteSpace(id))
            throw new FormatException($"Mensagem WPPConnect sem identificador (evento {evento}; chaves: {Chaves(root)}).");
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
        // #2155: na localização o body é a miniatura do mapa; ficam as coordenadas e a descrição do lugar.
        var localizacao = tipo == "location"
            ? LocalizacaoWhatsApp.Criar(Coordenada(root, "lat"), Coordenada(root, "lng"), Texto(root, "loc"), null)
            : null;
        if (localizacao is not null)
            texto = localizacao.Nome is { } lugar ? $"{lugar}; {localizacao.Coordenadas}" : localizacao.Coordenadas;
        if (!root.TryGetProperty("t", out var t) || !t.TryGetInt64(out var segundos))
            throw new FormatException("Mensagem WPPConnect sem data.");
        var quando = DateTimeOffset.FromUnixTimeSeconds(segundos);
        if (!eco && inicioDaCaptura != default && quando < inicioDaCaptura) return new([]);
        var nome = root.TryGetProperty("sender", out var remetente) ? Texto(remetente, "pushname") : null;
        var contatos = new ContatoWhatsApp[] { new(telefone, nome) };
        return new([new(sessao, rawBody, contatos,
            eco ? [] : [new(telefone, externo, quando, tipo, texto, midia, Texto(root, "mimetype"), null, null, localizacao)],
            [], eco ? [new(telefone, externo, quando, tipo, texto, midia, Texto(root, "mimetype"))] : [])]);
    }

    public static string? Texto(JsonElement root, string nome) =>
        root.ValueKind == JsonValueKind.Object && root.TryGetProperty(nome, out var valor) && valor.ValueKind == JsonValueKind.String
            ? valor.GetString() : null;

    /// <summary>O WPPConnect manda <c>lat</c>/<c>lng</c> como número ou como texto, conforme a versão.</summary>
    private static double? Coordenada(JsonElement root, string nome) =>
        !root.TryGetProperty(nome, out var valor) ? null
        : valor.ValueKind == JsonValueKind.Number && valor.TryGetDouble(out var numero) ? numero
        : valor.ValueKind == JsonValueKind.String
            && double.TryParse(valor.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var lido) ? lido
        : null;

    public static string? Id(JsonElement root) => Serializado(root, "id");

    /// <summary>Identificador do WhatsApp em texto ou no objeto com <c>_serialized</c>.</summary>
    public static string? Serializado(JsonElement root, string nome)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(nome, out var valor)) return null;
        return valor.ValueKind == JsonValueKind.String ? valor.GetString() : Texto(valor, "_serialized");
    }

    /// <summary>Grupo e transmissão são ignorados antes de exigir id (#2590).</summary>
    public static bool Grupo(JsonElement root) =>
        new[] { Serializado(root, "from"), Serializado(root, "to"), Serializado(root, "chatId") }.Any(c =>
            c is not null && (c.EndsWith("@g.us", StringComparison.Ordinal) || c.EndsWith("@broadcast", StringComparison.Ordinal)))
        || (root.TryGetProperty("isGroupMsg", out var grupo) && grupo.ValueKind == JsonValueKind.True);

    // Só nomes com cara de campo: chave numérica ou com telefone, que seria dado, fica de fora.
    private static string Chaves(JsonElement root) =>
        string.Join(",", root.EnumerateObject().Select(p => p.Name).Where(NomeDeCampo).Take(60));

    private static bool NomeDeCampo(string nome) =>
        nome.Length is > 0 and <= 40 && (char.IsAsciiLetter(nome[0]) || nome[0] == '_')
        && nome.All(c => char.IsAsciiLetterOrDigit(c) || c == '_');
}
