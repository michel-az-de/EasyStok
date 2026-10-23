namespace EasyStock.Api.Observability.Logging;

/// <summary>
/// Caminho da requisição como ele pode aparecer em log e span (I4.5, #1698; #1725-3: o token da comanda sai como
/// <c>{token}</c>). Em webhook vale o molde da rota
/// (<c>/api/webhooks/{provedor}</c>), não o caminho: é o provedor quem monta essa URL, e segmento de URL de
/// webhook costuma carregar identificador ou segredo.
/// </summary>
public static class CaminhoDoLog
{
    public const string PrefixoDeWebhook = "/api/webhooks";
    public const string WebhookSemRota = PrefixoDeWebhook + "/{...}";

    public static bool EhWebhook(HttpContext contexto) =>
        contexto.Request.Path.StartsWithSegments(PrefixoDeWebhook, StringComparison.OrdinalIgnoreCase);

    /// <summary>Prefixo das rotas públicas da comanda: o segmento seguinte é o token de acesso (#1725-3).</summary>
    public const string PrefixoDaComanda = "/api/storefront/cardapio-conversa";

    /// <summary>O caminho cru carrega segredo: webhook (montado pelo provedor) ou token da comanda.</summary>
    public static bool TemSegredo(HttpContext contexto) =>
        EhWebhook(contexto) || contexto.Request.Path.StartsWithSegments(PrefixoDaComanda, StringComparison.OrdinalIgnoreCase);

    public static string De(HttpContext contexto)
    {
        if (contexto.Request.Path.StartsWithSegments(PrefixoDaComanda, StringComparison.OrdinalIgnoreCase, out var resto))
            return PrefixoDaComanda + MascararPrimeiroSegmento(resto.Value);
        if (!EhWebhook(contexto)) return contexto.Request.Path.Value ?? string.Empty;
        var molde = (contexto.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText;
        return string.IsNullOrEmpty(molde) ? WebhookSemRota : "/" + molde.TrimStart('/');
    }

    // "/AbC123/comanda" vira "/{token}/comanda": só o token sai; o resto da rota fica.
    private static string MascararPrimeiroSegmento(string? resto)
    {
        if (string.IsNullOrEmpty(resto) || resto == "/") return resto ?? string.Empty;
        var depois = resto.IndexOf('/', 1);
        return "/{token}" + (depois < 0 ? string.Empty : resto[depois..]);
    }
}