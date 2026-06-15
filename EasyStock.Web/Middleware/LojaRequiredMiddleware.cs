using EasyStock.Web.Infrastructure;
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
        // Rotas acessiveis SEM loja selecionada — devem espelhar
        // BaseController.ControllersAllowedWithoutLoja {Lojas, Assinatura, Kds}.
        // Sem este alinhamento, qualquer redirect para /assinatura (ex.: criacao
        // de loja bloqueada por limite de plano OU pelo SubscriptionGate da Api,
        // que devolve 402) era imediatamente devolvido pelo middleware a
        // /auth/selecionar-loja — loop de onboarding sem feedback
        // (BUG-LOJA-ONBOARDING-001). "/lojas" cobre o wizard /lojas/criar.
        "/lojas",
        "/assinatura",
        "/kds",
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
            // BUG-65 (#452): AJAX recebe 409 + header no-store em vez de redirect HTML;
            // o esFetch no cliente leva pra selecionar-loja. Navegação segue no redirect.
            if (AjaxRequest.WantsJson(context.Request))
            {
                context.Response.StatusCode = StatusCodes.Status409Conflict;
                context.Response.Headers["X-EasyStok-Auth"] = "no-store";
                return;
            }
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
