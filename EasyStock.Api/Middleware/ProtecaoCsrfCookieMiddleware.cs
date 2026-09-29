namespace EasyStock.Api.Middleware;

/// <summary>
/// Defesa CSRF das rotas autenticadas por cookie do storefront (issue #1088, ADR-0053).
///
/// <para>
/// A Api autentica quase tudo por JWT bearer, que o navegador nao anexa sozinho. A excecao sao os
/// cookies <c>__Host-cdb_*</c> do storefront (sessao do cliente, ADR-0012, e link de avaliacao):
/// esses o navegador anexa em qualquer request para o host, inclusive a disparada por outro site.
/// <c>SameSite=Lax</c> barra o POST cross-site, mas nao o same-site (subdominio irmao).
/// </para>
///
/// <para>
/// Regra: request que altera estado e carrega cookie <c>__Host-cdb_*</c> precisa provar a mesma
/// origem. Com Fetch Metadata, <c>Sec-Fetch-Site</c> tem de ser <c>same-origin</c> ou <c>none</c>.
/// Sem ele, o <c>Origin</c>, se vier, tem de bater com o <c>Host</c>. Sem nenhum dos dois o
/// cliente nao e navegador, e CSRF nao se aplica. O front do storefront e same-origin (Caddy faz
/// proxy de <c>/api</c>), entao nao precisa mandar nada novo. A decisao e pela presenca do cookie,
/// nao pelo caminho: o Web e o Admin chamam <c>/api/storefront/pedidos</c> por bearer, de outra
/// origem, e seguem livres.
/// </para>
/// </summary>
public sealed class ProtecaoCsrfCookieMiddleware(RequestDelegate next, ILogger<ProtecaoCsrfCookieMiddleware> logger)
{
    private const string PrefixoCookieStorefront = "__Host-cdb_";

    public async Task InvokeAsync(HttpContext context)
    {
        var request = context.Request;

        if (HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method)
            || HttpMethods.IsOptions(request.Method) || HttpMethods.IsTrace(request.Method)
            || !CarregaCookieDoStorefront(request)
            || VemDaMesmaOrigem(request))
        {
            await next(context);
            return;
        }

        logger.LogWarning(
            "CSRF: {Method} {Path} com cookie do storefront recusado. Sec-Fetch-Site={SecFetchSite} Origin={Origin}",
            request.Method, request.Path, request.Headers["Sec-Fetch-Site"].ToString(), request.Headers.Origin.ToString());

        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await context.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Status = StatusCodes.Status403Forbidden,
            Title = "Origem não permitida",
            Detail = "Requisição com cookie de sessão precisa partir da própria loja.",
        }, options: null, contentType: "application/problem+json");
    }

    private static bool CarregaCookieDoStorefront(HttpRequest request)
    {
        foreach (var cookie in request.Cookies)
            if (cookie.Key.StartsWith(PrefixoCookieStorefront, StringComparison.Ordinal))
                return true;
        return false;
    }

    private static bool VemDaMesmaOrigem(HttpRequest request)
    {
        var secFetchSite = request.Headers["Sec-Fetch-Site"].ToString();
        if (secFetchSite.Length > 0)
            return secFetchSite is "same-origin" or "none";

        var origin = request.Headers.Origin.ToString();
        if (origin.Length == 0)
            return true;

        return Uri.TryCreate(origin, UriKind.Absolute, out var uri)
            && string.Equals(uri.Authority, request.Host.Value, StringComparison.OrdinalIgnoreCase);
    }
}
