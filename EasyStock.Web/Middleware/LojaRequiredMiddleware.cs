using EasyStock.Web.Services;

namespace EasyStock.Web.Middleware;

/// <summary>
/// Garante que usuario logado sem LojaId selecionada cai no wizard de loja
/// (/auth/selecionar-loja) em vez de navegar livre pelo app sem multi-tenant
/// ancorado. Sem isso, SuperAdmin sem loja vinculada conseguia abrir Dashboard,
/// /produtos/novo etc. e disparar requests com X-Loja-ID vazio.
/// </summary>
public sealed class LojaRequiredMiddleware(RequestDelegate next)
{
    private static readonly string[] ExemptPrefixes =
    [
        "/auth/",
        "/error/",
        "/health",
        "/css/",
        "/js/",
        "/img/",
        "/lib/",
        "/_framework/",
        "/_blazor/",
        "/favicon",
        // Submit do wizard de criar primeira loja vive em /lojas/criar — sem essa
        // excecao o usuario sem LojaId voltaria pra /auth/selecionar-loja num loop.
        "/lojas/criar",
    ];

    public async Task InvokeAsync(HttpContext context, SessionService session)
    {
        var path = context.Request.Path.Value ?? "";
        var isAuthenticated = context.User.Identity?.IsAuthenticated == true;

        if (isAuthenticated
            && session.IsLoggedIn()
            && string.IsNullOrEmpty(session.GetLojaId())
            && !IsExempt(path))
        {
            context.Response.Redirect("/auth/selecionar-loja");
            return;
        }

        await next(context);
    }

    private static bool IsExempt(string path)
    {
        if (string.IsNullOrEmpty(path)) return true;
        foreach (var prefix in ExemptPrefixes)
        {
            if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}
