using Microsoft.Extensions.Configuration;

namespace EasyStock.Application.UseCases.Common;

/// <summary>
/// Sanea o <c>BaseUrl</c> usado para compor links de e-mail (reset de senha,
/// confirmação de cadastro). O valor chega pelo corpo da requisição e NÃO pode
/// ser confiado: um atacante que aponte o link para um host próprio recebe o
/// token da vítima (password reset poisoning, #765). Só devolve o host quando
/// ele bate com uma origem explicitamente confiável; caso contrário devolve
/// <c>null</c> e o chamador cai no fluxo sem link (token puro / sem envio).
/// </summary>
public static class LinkBaseUrlResolver
{
    /// <summary>
    /// Origens confiáveis = <c>Auth:TrustedLinkOrigins</c> (allowlist dedicada,
    /// preferencial) unida a <c>Cors:AllowedOrigins</c> (fallback: os hosts de
    /// frontend já autorizados). Operadores em hosts fora dessas listas (ex.: VM
    /// via sslip.io) devem adicionar o host do Web em <c>Auth:TrustedLinkOrigins</c>;
    /// enquanto não o fizerem, o e-mail degrada para token puro (seguro), nunca
    /// para um host arbitrário.
    /// </summary>
    public static string? ResolveTrusted(string? requested, IConfiguration configuration)
    {
        var trusted = LerOrigens(configuration, "Auth:TrustedLinkOrigins")
            .Concat(LerOrigens(configuration, "Cors:AllowedOrigins"));
        return ResolveTrusted(requested, trusted);
    }

    // GetChildren().Value evita a dependencia do pacote Configuration.Binder (.Get<T>()).
    private static IEnumerable<string?> LerOrigens(IConfiguration configuration, string secao) =>
        configuration.GetSection(secao).GetChildren().Select(c => c.Value);

    /// <summary>
    /// Overload testável sem <see cref="IConfiguration"/>. Compara esquema + host
    /// + porta (ignora path/query) contra cada origem confiável.
    /// </summary>
    public static string? ResolveTrusted(string? requested, IEnumerable<string?>? trustedOrigins)
    {
        if (string.IsNullOrWhiteSpace(requested)) return null;
        if (!Uri.TryCreate(requested, UriKind.Absolute, out var requestedUri)) return null;
        if (trustedOrigins is null) return null;

        foreach (var origem in trustedOrigins)
        {
            if (string.IsNullOrWhiteSpace(origem)) continue;
            if (!Uri.TryCreate(origem, UriKind.Absolute, out var origemUri)) continue;

            if (Uri.Compare(requestedUri, origemUri,
                    UriComponents.SchemeAndServer, UriFormat.Unescaped,
                    StringComparison.OrdinalIgnoreCase) == 0)
            {
                return requested.TrimEnd('/');
            }
        }

        return null;
    }
}
