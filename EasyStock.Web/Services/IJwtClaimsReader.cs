namespace EasyStock.Web.Services;

/// <summary>
/// Le claims do payload JWT SEM validar assinatura. Use apenas para tokens
/// recebidos diretamente da API confiavel (response de /auth/login ou /auth/refresh).
/// A validacao real (assinatura, exp, iss, aud) e responsabilidade do servidor —
/// o Web nao tem a chave secreta de assinatura.
///
/// NAO USE para tokens vindos de fonte externa nao-confiavel (ex: form body em
/// endpoint AllowAnonymous) — nesses casos, valide via chamada de API antes.
/// </summary>
public interface IJwtClaimsReader
{
    /// <summary>
    /// Tenta ler uma claim string do payload. Retorna null em qualquer falha:
    /// token mal-formado, payload nao-decodificavel, claim ausente ou nao-string.
    /// </summary>
    string? TryReadClaim(string token, string claimType);
}
