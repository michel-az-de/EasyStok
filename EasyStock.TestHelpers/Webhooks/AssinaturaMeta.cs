using System.Security.Cryptography;
using System.Text;

namespace EasyStock.TestHelpers.Webhooks;

/// <summary>
/// Gera o <c>X-Hub-Signature-256</c> dos webhooks da Meta (WhatsApp, Messenger e Instagram) como o provedor
/// assina (#1634): <c>sha256=</c> + HMAC SHA-256 do corpo bruto com o App Secret, hexadecimal minúsculo.
/// É o formato que <c>EasyStock.Api/Controllers/Webhooks/AssinaturaWebhookMeta.cs</c> valida.
/// </summary>
public static class AssinaturaMeta
{
    public const string Header = "X-Hub-Signature-256";

    /// <summary>Valor do cabeçalho para o <paramref name="corpo"/> exato que será enviado (um byte diferente invalida).</summary>
    public static string Gerar(string corpo, string appSecret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(appSecret));
        return "sha256=" + Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(corpo))).ToLowerInvariant();
    }
}
