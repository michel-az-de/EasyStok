namespace EasyStock.Admin.Helpers;

/// <summary>
/// Mapa canonico de status de assinatura/tenant para a variante semantica do &lt;es-badge&gt;.
/// Consolida o switch que vivia inline em Tenants/Index e no header do Detail (issue 730),
/// para que lista e detalhe nunca divirjam de cor/semantica.
/// </summary>
public static class StatusBadgeMap
{
    /// <summary>
    /// Variante do badge: <c>ok</c> (ativa), <c>warn</c> (suspensa), <c>crit</c> (cancelada
    /// ou trial expirado sem plano), <c>neutral</c> (expirada/desconhecido).
    /// </summary>
    public static string Variant(string? status, bool trialExpirado = false)
    {
        if (trialExpirado) return "crit";
        return (status ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "ativa" => "ok",
            "suspensa" => "warn",
            "cancelada" => "crit",
            "expirada" => "neutral",
            _ => "neutral"
        };
    }
}
