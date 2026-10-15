namespace EasyStock.Infra.Notifications.Push;

/// <summary>
/// Endpoint de inscrição Web Push aceito (#1508): URL absoluta https, porta padrão, sem credencial embutida,
/// no host de um serviço de push de navegador conhecido e dentro do tamanho da coluna. Sem isso o worker faz
/// POST para qualquer URL gravada (SSRF cego). Vale no cadastro (PwaPushController) e no envio (WebPushCanal).
/// </summary>
public static class EndpointPushPermitido
{
    /// <summary>Mesmo limite da coluna <c>web_push_subscriptions.endpoint</c>.</summary>
    public const int TamanhoMaximo = 2000;

    // Chrome/Edge antigo (FCM), Firefox (autopush) e Safari (APNs web).
    private static readonly string[] HostsExatos = ["fcm.googleapis.com", "updates.push.services.mozilla.com"];

    // Subdomínios dos serviços: web.push.apple.com, wns2-*.notify.windows.com (Edge/Windows), *.push.services.mozilla.com.
    private static readonly string[] SufixosDeHost = [".push.apple.com", ".notify.windows.com", ".push.services.mozilla.com"];

    public static bool Valido(string? endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint) || endpoint.Length > TamanhoMaximo)
            return false;
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri))
            return false;
        if (uri.Scheme != Uri.UriSchemeHttps || !uri.IsDefaultPort || uri.UserInfo.Length > 0)
            return false;

        var host = uri.IdnHost.ToLowerInvariant();
        return HostsExatos.Contains(host)
            || SufixosDeHost.Any(sufixo => host.EndsWith(sufixo, StringComparison.Ordinal));
    }
}
