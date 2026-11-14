using System.Text.Json;
using System.Text.Json.Nodes;
using EasyStock.Application.Ports.Output.Atendimento;

namespace EasyStock.Application.UseCases.Atendimento.Webhook;

/// <summary>
/// #2590: o WPPConnect monta o evento com <c>id = msg.id._serialized</c>. Quando esse campo ainda não existe no
/// instante do evento, a chave some do JSON. O id real é recuperado nas mensagens recentes da conversa, para a
/// deduplicação continuar valendo. Sem correspondência única, a falha é retentável: um id inventado faria o robô
/// responder de novo quando o mesmo evento voltasse com o id real.
/// </summary>
public static class IdAusenteWppConnect
{
    public static async Task<string> CompletarAsync(string rawBody, CredencialWppConnect sessao, IWppConnectSessaoClient cliente,
        CancellationToken ct = default)
    {
        string chat;
        long quando;
        bool deMim;
        string? tipo, corpo;
        using (var doc = JsonDocument.Parse(rawBody))
        {
            var root = doc.RootElement;
            if (WppConnectWebhookParser.Texto(root, "event") is not ("onmessage" or "unreadmessages")
                || !string.IsNullOrWhiteSpace(WppConnectWebhookParser.Id(root)) || WppConnectWebhookParser.Grupo(root))
                return rawBody;
            deMim = root.TryGetProperty("fromMe", out var f) && f.ValueKind == JsonValueKind.True;
            var conversa = WppConnectWebhookParser.Serializado(root, "chatId")
                ?? WppConnectWebhookParser.Texto(root, deMim ? "to" : "from");
            // Sem conversa ou sem data não há como procurar: o parser responde 400 com as chaves do evento.
            if (string.IsNullOrWhiteSpace(conversa) || !root.TryGetProperty("t", out var t) || !t.TryGetInt64(out quando))
                return rawBody;
            chat = conversa;
            tipo = WppConnectWebhookParser.Texto(root, "type");
            corpo = WppConnectWebhookParser.Texto(root, "body");
        }

        var id = Escolher(await cliente.ListarMensagensRecentesAsync(sessao, chat, ct), quando, deMim, tipo, corpo)
            ?? throw new WhatsAppCloudException(-2590,
                "O WPPConnect enviou a mensagem sem id e ela ainda não aparece na conversa. O evento será tentado novamente.",
                false, 503);
        var json = JsonNode.Parse(rawBody)!.AsObject();
        json["id"] = id;
        return json.ToJsonString();
    }

    private static string? Escolher(IReadOnlyList<MensagemRecenteWppConnect> recentes, long quando, bool deMim,
        string? tipo, string? corpo)
    {
        var candidatas = recentes.Where(m => m.Quando == quando && m.DeMim == deMim && !string.IsNullOrWhiteSpace(m.Id)).ToList();
        if (candidatas.Count > 1) candidatas = candidatas.Where(m => m.Tipo == tipo && m.Corpo == corpo).ToList();
        return candidatas.Count == 1 ? candidatas[0].Id : null;
    }
}
