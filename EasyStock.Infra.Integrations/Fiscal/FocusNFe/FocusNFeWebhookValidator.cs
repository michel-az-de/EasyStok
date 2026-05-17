using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace EasyStock.Infra.Integrations.Fiscal.FocusNFe;

/// <summary>
/// Valida assinatura HMAC-SHA256 do webhook Focus NFe. O secret e por ambiente
/// (sandbox vs producao) e configurado em <see cref="FocusNFeOptions.WebhookSecret"/>.
/// Header recebido pelo controller: <c>X-Focus-Signature</c> (hex lowercase).
///
/// <para>
/// <b>Seguranca:</b> usa <see cref="CryptographicOperations.FixedTimeEquals(ReadOnlySpan{byte}, ReadOnlySpan{byte})"/>
/// para prevenir timing attacks. NUNCA comparar com <c>==</c>.
/// </para>
/// </summary>
public sealed class FocusNFeWebhookValidator(IOptions<FocusNFeOptions> options)
{
    private readonly FocusNFeOptions _options = options.Value;

    /// <summary>
    /// Valida assinatura. Retorna <c>true</c> se header bate com HMAC(secret, body).
    /// Retorna <c>false</c> se secret nao configurado, header ausente, ou hash divergente.
    /// </summary>
    public bool ValidarAssinatura(string? signatureHeader, byte[] bodyBytes)
    {
        if (string.IsNullOrWhiteSpace(_options.WebhookSecret))
            return false;

        if (string.IsNullOrWhiteSpace(signatureHeader) || bodyBytes is null || bodyBytes.Length == 0)
            return false;

        byte[] expected;
        try
        {
            expected = ParseHex(signatureHeader.Trim());
        }
        catch
        {
            return false;
        }

        var keyBytes = Encoding.UTF8.GetBytes(_options.WebhookSecret);
        using var hmac = new HMACSHA256(keyBytes);
        var computed = hmac.ComputeHash(bodyBytes);

        if (expected.Length != computed.Length) return false;
        return CryptographicOperations.FixedTimeEquals(expected, computed);
    }

    private static byte[] ParseHex(string hex)
    {
        if (hex.Length % 2 != 0)
            throw new FormatException("Hex com tamanho impar.");

        var bytes = new byte[hex.Length / 2];
        for (var i = 0; i < bytes.Length; i++)
        {
            bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
        }
        return bytes;
    }
}
