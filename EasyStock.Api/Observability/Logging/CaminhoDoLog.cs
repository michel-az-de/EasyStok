namespace EasyStock.Api.Observability.Logging;

/// <summary>
/// Caminho da requisição como ele pode aparecer em log e span (I4.5, #1698). Em webhook vale o molde da rota
/// (<c>/api/webhooks/{provedor}</c>), não o caminho: é o provedor quem monta essa URL, e segmento de URL de
/// webhook costuma carregar identificador ou segredo.
/// </summary>
public static class CaminhoDoLog
{
    public const string PrefixoDeWebhook = "/api/webhooks";
    public const string WebhookSemRota = PrefixoDeWebhook + "/{...}";

    public static bool EhWebhook(HttpContext contexto) =>
        contexto.Request.Path.StartsWithSegments(PrefixoDeWebhook, StringComparison.OrdinalIgnoreCase);

    public static string De(HttpContext contexto)
    {
        if (!EhWebhook(contexto)) return contexto.Request.Path.Value ?? string.Empty;
        var molde = (contexto.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText;
        return string.IsNullOrEmpty(molde) ? WebhookSemRota : "/" + molde.TrimStart('/');
    }
}