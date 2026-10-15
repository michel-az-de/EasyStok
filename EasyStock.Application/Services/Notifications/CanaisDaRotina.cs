using System.Text.Json;
using System.Text.Json.Serialization;
using EasyStock.Domain.Entities.Notifications;
using EasyStock.Domain.Enums.Notifications;

namespace EasyStock.Application.Services.Notifications;

/// <summary>Como a rotina entrega nos canais permitidos (N5), lido de <c>ParametrosJson.modoCanais</c>.</summary>
public enum ModoCanais
{
    /// <summary>Padrão: um canal por vez, os seguintes só se o anterior falhar.</summary>
    Fallback,

    /// <summary>Uma mensagem por canal permitido, cada uma com template, contato e metadados próprios.</summary>
    Todos
}

/// <summary>Leitura dos canais e do modo da rotina, e da restrição por evento (<c>canais</c> no payload).</summary>
public static class CanaisDaRotina
{
    /// <summary>Chave do payload que restringe os canais da rotina naquele evento (N5).</summary>
    public const string CanaisPayload = "canais";

    /// <summary>Chave de <c>ParametrosJson</c> com o modo de entrega (N5).</summary>
    public const string ModoCanaisParametro = "modoCanais";

    private static readonly JsonSerializerOptions Opcoes = new() { Converters = { new JsonStringEnumConverter() } };

    /// <summary>Canais da rotina em ordem de preferência; JSON inválido vale lista vazia.</summary>
    public static IReadOnlyList<CanalNotificacao> Ler(RotinaNotificacao rotina) => Ler(rotina.CanaisOrdemFallbackJson);

    public static IReadOnlyList<CanalNotificacao> Ler(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<List<CanalNotificacao>>(json, Opcoes) ?? [];
        }
        catch
        {
            return [];
        }
    }

    public static string Serializar(IEnumerable<CanalNotificacao> canais) =>
        JsonSerializer.Serialize(canais, Opcoes);

    /// <summary>Sem a chave (ou com valor desconhecido) o modo é o fallback de hoje.</summary>
    public static ModoCanais LerModo(string parametrosJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(parametrosJson);
            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty(ModoCanaisParametro, out var modo)
                && modo.ValueKind == JsonValueKind.String
                && string.Equals(modo.GetString(), "todos", StringComparison.OrdinalIgnoreCase))
                return ModoCanais.Todos;
        }
        catch (JsonException) { /* parâmetros inválidos: fallback */ }

        return ModoCanais.Fallback;
    }

    /// <summary>
    /// A restrição de canais do evento, ou <c>null</c> quando o payload não traz a chave <c>canais</c>. Nomes
    /// desconhecidos são ignorados; chave presente com lista sem canal válido restringe a nada.
    /// </summary>
    public static IReadOnlyList<CanalNotificacao>? LerRestricao(string payloadJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(payloadJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object
                || !doc.RootElement.TryGetProperty(CanaisPayload, out var lista))
                return null;

            var canais = new List<CanalNotificacao>();
            if (lista.ValueKind == JsonValueKind.Array)
                foreach (var item in lista.EnumerateArray())
                    if (item.ValueKind == JsonValueKind.String
                        && Enum.TryParse<CanalNotificacao>(item.GetString(), ignoreCase: true, out var canal))
                        canais.Add(canal);
            return canais;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
