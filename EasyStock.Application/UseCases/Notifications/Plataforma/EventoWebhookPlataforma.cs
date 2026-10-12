using System.Text.Json;

namespace EasyStock.Application.UseCases.Notifications.Plataforma;

/// <summary>Status de uma mensagem enviada pelo número de plataforma, já extraído do webhook (N6).</summary>
public sealed record StatusPlataforma(
    string PhoneNumberId,
    string Wamid,
    string Status,
    string? Opaco,
    int? CodigoErro,
    string? MensagemErro,
    string? CategoriaPreco);

/// <summary>Mensagem que alguém mandou ao número de plataforma (N6).</summary>
public sealed record MensagemRecebidaPlataforma(string PhoneNumberId, string De, string Wamid, string Tipo);

public sealed record EventoWebhookPlataforma(
    IReadOnlyList<StatusPlataforma> Statuses, IReadOnlyList<MensagemRecebidaPlataforma> Mensagens);

/// <summary>
/// Parser tolerante do <c>messages</c> do webhook, só do que a plataforma usa. Não depende do parser do atendimento: a
/// fronteira entre os dois é testada.
/// </summary>
public static class WebhookPlataformaParser
{
    public static EventoWebhookPlataforma Parse(string rawBody)
    {
        var statuses = new List<StatusPlataforma>();
        var mensagens = new List<MensagemRecebidaPlataforma>();
        using var doc = JsonDocument.Parse(rawBody);

        foreach (var value in Valores(doc.RootElement))
        {
            var phoneNumberId = value.TryGetProperty("metadata", out var meta) ? Texto(meta, "phone_number_id") ?? "" : "";

            if (value.TryGetProperty("statuses", out var ss) && ss.ValueKind == JsonValueKind.Array)
                foreach (var s in ss.EnumerateArray())
                    statuses.Add(ParseStatus(phoneNumberId, s));

            if (value.TryGetProperty("messages", out var ms) && ms.ValueKind == JsonValueKind.Array)
                foreach (var m in ms.EnumerateArray())
                    mensagens.Add(new MensagemRecebidaPlataforma(
                        phoneNumberId, Texto(m, "from") ?? "", Texto(m, "id") ?? "", Texto(m, "type") ?? "unsupported"));
        }

        return new EventoWebhookPlataforma(statuses, mensagens);
    }

    private static StatusPlataforma ParseStatus(string phoneNumberId, JsonElement s)
    {
        int? codigo = null;
        string? mensagemErro = null;
        if (s.TryGetProperty("errors", out var erros) && erros.ValueKind == JsonValueKind.Array && erros.GetArrayLength() > 0)
        {
            var e = erros[0];
            if (e.TryGetProperty("code", out var c) && c.TryGetInt32(out var n)) codigo = n;
            mensagemErro = Texto(e, "title") ?? Texto(e, "message");
        }

        var categoria = s.TryGetProperty("pricing", out var preco) ? Texto(preco, "category") : null;
        return new StatusPlataforma(
            phoneNumberId, Texto(s, "id") ?? "", Texto(s, "status") ?? "", Texto(s, "biz_opaque_callback_data"),
            codigo, mensagemErro, categoria);
    }

    private static IEnumerable<JsonElement> Valores(JsonElement raiz)
    {
        if (!raiz.TryGetProperty("entry", out var entradas) || entradas.ValueKind != JsonValueKind.Array) yield break;
        foreach (var entrada in entradas.EnumerateArray())
        {
            if (!entrada.TryGetProperty("changes", out var mudancas) || mudancas.ValueKind != JsonValueKind.Array) continue;
            foreach (var mudanca in mudancas.EnumerateArray())
                if (mudanca.TryGetProperty("value", out var v) && v.ValueKind == JsonValueKind.Object)
                    yield return v;
        }
    }

    private static string? Texto(JsonElement e, string nome) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(nome, out var p) && p.ValueKind == JsonValueKind.String
            ? p.GetString()
            : null;
}
