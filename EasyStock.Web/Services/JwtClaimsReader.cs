using System.Text.Json;

namespace EasyStock.Web.Services;

/// <summary>
/// Implementacao stateless de <see cref="IJwtClaimsReader"/>. Decodifica o payload
/// (parte 1 do JWT) como base64url + JSON. Sem deps externas, registrar como Singleton.
/// </summary>
public sealed class JwtClaimsReader : IJwtClaimsReader
{
    public string? TryReadClaim(string token, string claimType)
    {
        if (string.IsNullOrEmpty(token)) return null;

        var parts = token.Split('.');
        if (parts.Length < 2) return null;

        var payload = parts[1];
        if (string.IsNullOrEmpty(payload)) return null;

        // base64url → base64: aplicar padding + trocar chars URL-safe.
        payload = (payload.Length % 4) switch
        {
            2 => payload + "==",
            3 => payload + "=",
            _ => payload
        };
        payload = payload.Replace('-', '+').Replace('_', '/');

        try
        {
            var bytes = Convert.FromBase64String(payload);
            using var doc = JsonDocument.Parse(bytes);
            return doc.RootElement.TryGetProperty(claimType, out var value)
                && value.ValueKind == JsonValueKind.String
                    ? value.GetString()
                    : null;
        }
        catch (FormatException) { return null; }  // base64 malformado
        catch (JsonException) { return null; }    // payload nao-JSON
    }
}
