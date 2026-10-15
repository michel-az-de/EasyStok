using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace EasyStock.Infra.Integrations.Logistica.Lalamove;

/// <summary>
/// Assinatura HMAC da API v3 da Lalamove (https://developers.lalamove.com/, "Authentication").
/// Peça reutilizável: o teste de conexão da F16 (#1246) e o gateway de entregas da F17 (#1247).
/// <list type="bullet">
///   <item>Texto bruto: <c>"{ts}\r\n{METODO}\r\n{PATH}\r\n\r\n{BODY}"</c>, com o PATH incluindo <c>/v3</c>.</item>
///   <item>Assinatura: HMAC-SHA256 do texto com a secret, em hexadecimal minúsculo.</item>
///   <item>Cabeçalho: <c>Authorization: hmac {key}:{ts}:{assinatura}</c>, ts em milissegundos.</item>
/// </list>
/// A secret nunca sai daqui: nem em exceção, nem em log.
/// </summary>
public static class AssinadorLalamove
{
    public static string Assinar(string secret, long timestampMs, string metodo, string caminho, string? corpo)
    {
        ArgumentException.ThrowIfNullOrEmpty(secret);
        ArgumentException.ThrowIfNullOrEmpty(metodo);
        ArgumentException.ThrowIfNullOrEmpty(caminho);

        var bruto = string.Create(CultureInfo.InvariantCulture,
            $"{timestampMs}\r\n{metodo.ToUpperInvariant()}\r\n{caminho}\r\n\r\n{corpo ?? string.Empty}");
        var chave = Encoding.UTF8.GetBytes(secret);
        try
        {
            return Convert.ToHexStringLower(HMACSHA256.HashData(chave, Encoding.UTF8.GetBytes(bruto)));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(chave);
        }
    }

    public static string Autorizacao(string apiKey, string secret, long timestampMs, string metodo, string caminho, string? corpo)
    {
        ArgumentException.ThrowIfNullOrEmpty(apiKey);
        return string.Create(CultureInfo.InvariantCulture,
            $"hmac {apiKey}:{timestampMs}:{Assinar(secret, timestampMs, metodo, caminho, corpo)}");
    }
}
