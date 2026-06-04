namespace EasyStock.Web.Infrastructure;

/// <summary>
/// Detecção de requisições programáticas (fetch/XHR) que esperam JSON em vez de
/// uma página HTML. Usado para responder 401/403/409 a chamadas AJAX em vez de
/// redirecionar (302 -> HTML de login), que o fetch seguiria e quebraria em
/// <c>r.json()</c>. Ver BUG-65 (#452) e wwwroot/js/es-fetch.js.
/// </summary>
public static class AjaxRequest
{
    public static bool WantsJson(HttpRequest request)
    {
        // Sinal explícito do wrapper esFetch ('fetch') e dos call-sites legados
        // ('XMLHttpRequest'); qualquer valor presente conta como AJAX.
        if (request.Headers.TryGetValue("X-Requested-With", out var xrw)
            && !string.IsNullOrEmpty(xrw.ToString()))
            return true;

        // Fallback: Accept: application/json (convenção do api.js).
        return request.Headers.Accept
            .Any(a => a is not null && a.Contains("application/json", StringComparison.OrdinalIgnoreCase));
    }
}
