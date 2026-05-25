using System.Security.Cryptography;
using System.Text;

namespace EasyStock.Application.UseCases.Storefront.Webhook;

/// <summary>
/// Valida HMAC-SHA256 do payload de webhook MercadoPago.
///
/// <para>
/// <strong>Constant-time comparison</strong> via
/// <see cref="CryptographicOperations.FixedTimeEquals"/> — proteção contra ataques de tempo.
/// </para>
///
/// <para>
/// Aceita assinaturas em lowercase ou uppercase (toleramos variações do MP). Não toleramos
/// prefixos como <c>sha256=</c> — caller deve normalizar antes (raro no MP, comum em GitHub).
/// </para>
/// </summary>
public sealed class MpHmacValidator
{
    public bool EhValido(byte[] payload, string assinaturaHex, string secret)
    {
        if (payload is null || payload.Length == 0) return false;
        if (string.IsNullOrWhiteSpace(assinaturaHex)) return false;
        if (string.IsNullOrWhiteSpace(secret)) return false;

        byte[] esperadoBytes;
        try
        {
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
            esperadoBytes = hmac.ComputeHash(payload);
        }
        catch
        {
            return false;
        }

        byte[] recebidoBytes;
        try
        {
            recebidoBytes = Convert.FromHexString(assinaturaHex.Trim());
        }
        catch (FormatException)
        {
            return false;
        }

        // Constant-time. Comprimentos diferentes → FixedTimeEquals retorna false sem leak.
        if (recebidoBytes.Length != esperadoBytes.Length) return false;
        return CryptographicOperations.FixedTimeEquals(recebidoBytes, esperadoBytes);
    }
}
