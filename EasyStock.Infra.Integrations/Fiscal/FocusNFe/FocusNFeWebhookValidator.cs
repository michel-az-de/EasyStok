using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace EasyStock.Infra.Integrations.Fiscal.FocusNFe;

/// <summary>
/// Valida assinatura HMAC-SHA256 (base64) do webhook Focus.
/// FixedTimeEquals previne ataques de timing.
/// </summary>
public sealed class FocusNFeWebhookValidator(IOptions<FocusNFeOptions> options)
{
    private readonly FocusNFeOptions _opt = options.Value;

    public bool ValidarHmac(string body, string? headerSignature)
    {
        if (string.IsNullOrEmpty(headerSignature)) return false;
        if (string.IsNullOrEmpty(_opt.WebhookSecret)) return false;

        var key = Encoding.UTF8.GetBytes(_opt.WebhookSecret);
        var msg = Encoding.UTF8.GetBytes(body ?? "");
        var computed = Convert.ToBase64String(HMACSHA256.HashData(key, msg));

        var lhs = Encoding.UTF8.GetBytes(computed);
        var rhs = Encoding.UTF8.GetBytes(headerSignature);
        return lhs.Length == rhs.Length && CryptographicOperations.FixedTimeEquals(lhs, rhs);
    }
}
