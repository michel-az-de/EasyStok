using System.Text;
using System.Text.Json;

namespace EasyStok.Mobile.Services;

/// <summary>
/// Decoder leve do payload JWT — sem validar assinatura (servidor faz isso).
/// Quero apenas ler claims (nivel, empresaId, permissao) para gating de UI.
/// </summary>
public static class JwtParser
{
    public static IReadOnlyDictionary<string, JsonElement> Decode(string jwt)
    {
        var parts = jwt.Split('.');
        if (parts.Length < 2)
            return new Dictionary<string, JsonElement>();

        var payload = parts[1];
        // Base64Url -> Base64
        payload = payload.Replace('-', '+').Replace('_', '/');
        switch (payload.Length % 4)
        {
            case 2: payload += "=="; break;
            case 3: payload += "="; break;
        }

        var bytes = Convert.FromBase64String(payload);
        var json = Encoding.UTF8.GetString(bytes);
        using var doc = JsonDocument.Parse(json);

        var dict = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        foreach (var prop in doc.RootElement.EnumerateObject())
            dict[prop.Name] = prop.Value.Clone();
        return dict;
    }

    public static string? GetString(IReadOnlyDictionary<string, JsonElement> claims, string name) =>
        claims.TryGetValue(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    public static IReadOnlyList<string> GetStringArray(IReadOnlyDictionary<string, JsonElement> claims, string name)
    {
        if (!claims.TryGetValue(name, out var v)) return Array.Empty<string>();
        if (v.ValueKind == JsonValueKind.String) return new[] { v.GetString() ?? "" };
        if (v.ValueKind == JsonValueKind.Array)
        {
            var list = new List<string>();
            foreach (var el in v.EnumerateArray())
                if (el.ValueKind == JsonValueKind.String) list.Add(el.GetString() ?? "");
            return list;
        }
        return Array.Empty<string>();
    }
}
