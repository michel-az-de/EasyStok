using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace EasyStock.Api.Mobile.Security;

public sealed record PushEnvelope(string PayloadBase64, string SignatureBase64, string? PublicKeyBase64 = null);

/// <summary>Assina bytes exatos com domínio versionado, sem confiar na serialização do destinatário.</summary>
public static class PushSignature
{
    public static byte[] Decode(string? value, int maximum)
    {
        if (string.IsNullOrEmpty(value) || value.Length > maximum * 2) throw new FormatException("Envelope inválido.");
        var bytes = Convert.FromBase64String(value);
        if (bytes.Length > maximum || Convert.ToBase64String(bytes) != value) throw new FormatException("Base64 inválida.");
        return bytes;
    }
    public static JsonDocument Payload(PushEnvelope envelope)
    {
        var bytes = Decode(envelope.PayloadBase64, 8192);
        _ = new UTF8Encoding(false, true).GetString(bytes);
        var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 4 });
        if (document.RootElement.ValueKind != JsonValueKind.Object) { document.Dispose(); throw new FormatException("Objeto obrigatório."); }
        var properties = document.RootElement.EnumerateObject().Select(p => p.Name).ToArray();
        if (properties.Distinct(StringComparer.Ordinal).Count() != properties.Length) { document.Dispose(); throw new FormatException("Campos repetidos."); }
        return document;
    }
    public static void Fields(JsonElement value, params string[] names)
    {
        if (!value.EnumerateObject().Select(p => p.Name).Order().SequenceEqual(names.Order())) throw new FormatException("Campos inválidos.");
    }
    public static string Text(JsonElement value, string name, int maximum)
    {
        if (!value.TryGetProperty(name, out var item) || item.ValueKind != JsonValueKind.String) throw new FormatException("Texto obrigatório.");
        var text = item.GetString()!;
        if (string.IsNullOrWhiteSpace(text) || text.Length > maximum || text.Any(c => char.IsControl(c) && c != '\n')) throw new FormatException("Texto inválido.");
        return text;
    }
    public static long Number(JsonElement value, string name)
    {
        if (!value.TryGetProperty(name, out var item) || !item.TryGetInt64(out var result) || result < 0) throw new FormatException("Número inválido.");
        return result;
    }
    public static string Installation(JsonElement value, string name = "installationId")
    {
        var id = Text(value, name, 64);
        if (!Regex.IsMatch(id, "^[a-f0-9]{64}$")) throw new FormatException("Instalação inválida.");
        return id;
    }
    public static void Fresh(long issuedAt, long now)
    {
        if (issuedAt < now - 300000 || issuedAt > now + 300000) throw new UnauthorizedAccessException("Assinatura fora da janela.");
    }
    public static void Kind(JsonElement value, string expected)
    {
        if (Text(value, "kind", 64) != expected) throw new FormatException("Domínio inválido.");
    }
    public static string Hash(string base64) => Convert.ToHexStringLower(SHA256.HashData(Decode(base64, 8192)));
    public static void Device(PushEnvelope envelope, string publicKey, string installation)
    {
        var keyBytes = Decode(publicKey, 256);
        if (Convert.ToHexStringLower(SHA256.HashData(keyBytes)) != installation) throw new UnauthorizedAccessException("Chave divergente.");
        using var key = ECDsa.Create();
        key.ImportSubjectPublicKeyInfo(keyBytes, out var read);
        if (read != keyBytes.Length || key.KeySize != 256 || key.ExportParameters(false).Curve.Oid.Value != "1.2.840.10045.3.1.7") throw new UnauthorizedAccessException("Curva inválida.");
        if (!key.VerifyData(Decode(envelope.PayloadBase64, 8192), Decode(envelope.SignatureBase64, 128), HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence)) throw new UnauthorizedAccessException("Assinatura inválida.");
    }
    public static void Publisher(PushEnvelope envelope, byte[] publicKey)
    {
        if (envelope.PublicKeyBase64 != null) throw new FormatException("Chave de publicação fixa.");
        using var key = RSA.Create();
        key.ImportSubjectPublicKeyInfo(publicKey, out var read);
        if (read != publicKey.Length || key.KeySize < 3072 || !key.VerifyData(Decode(envelope.PayloadBase64, 8192), Decode(envelope.SignatureBase64, 1024), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)) throw new UnauthorizedAccessException("Assinatura inválida.");
    }
    public static byte[] PublisherKey()
    {
        using var stream = typeof(PushSignature).Assembly.GetManifestResourceStream("Mobile.PushPublisherKey") ?? throw new InvalidOperationException("Chave pública ausente.");
        using var buffer = new MemoryStream(); stream.CopyTo(buffer); return buffer.ToArray();
    }
}
