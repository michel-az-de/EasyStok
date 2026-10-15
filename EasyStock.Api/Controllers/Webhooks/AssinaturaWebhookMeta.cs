using System.Security.Cryptography;
using System.Text;

namespace EasyStock.Api.Controllers.Webhooks;

/// <summary>
/// Assinatura dos webhooks da Meta (WhatsApp, Messenger e Instagram): <c>X-Hub-Signature-256</c> =
/// <c>sha256=</c> + HMAC-SHA256 do corpo bruto com o App Secret, comparado em tempo constante.
/// </summary>
public static class AssinaturaWebhookMeta
{
    public const string Header = "X-Hub-Signature-256";

    public static bool Valida(string? cabecalho, string rawBody, string appSecret)
    {
        if (string.IsNullOrEmpty(cabecalho) || string.IsNullOrEmpty(appSecret))
            return false;

        const string prefixo = "sha256=";
        if (!cabecalho.StartsWith(prefixo, StringComparison.OrdinalIgnoreCase))
            return false;

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(appSecret));
        var esperada = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(rawBody))).ToLowerInvariant();
        return IguaisEmTempoConstante(esperada, cabecalho[prefixo.Length..].ToLowerInvariant());
    }

    public static bool IguaisEmTempoConstante(string a, string b)
    {
        var ba = Encoding.UTF8.GetBytes(a);
        var bb = Encoding.UTF8.GetBytes(b);
        return ba.Length == bb.Length && CryptographicOperations.FixedTimeEquals(ba, bb);
    }
}