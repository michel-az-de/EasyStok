using System.Security.Cryptography;
using System.Text;

namespace EasyStock.Application.UseCases.Storefront.Auth;

/// <summary>
/// Calcula o fingerprint heurístico da sessão (ADR-0012).
///
/// <para>
/// Fingerprint = SHA-256 hex de (UserAgent + Accept-Language).
/// Determinístico — mesmo par de valores sempre produz o mesmo hash.
/// Usado pelo middleware para detectar possível session hijacking:
/// mudança de UA ou Accept-Language força re-login.
/// </para>
///
/// <para>
/// Retorna null se ambos os campos forem vazios/nulos — sessões sem
/// contexto de browser não têm fingerprint e o middleware ignora a verificação.
/// </para>
/// </summary>
public static class ClienteFingerprintCalculator
{
    /// <summary>
    /// Calcula SHA-256 hex de <c>userAgent + acceptLanguage</c>.
    /// Retorna null se ambos forem nulos ou vazios.
    /// </summary>
    public static string? Calcular(string? userAgent, string? acceptLanguage)
    {
        var ua = userAgent?.Trim() ?? string.Empty;
        var al = acceptLanguage?.Trim() ?? string.Empty;

        if (ua.Length == 0 && al.Length == 0)
            return null;

        var input = ua + "|" + al;
        var bytes = Encoding.UTF8.GetBytes(input);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
