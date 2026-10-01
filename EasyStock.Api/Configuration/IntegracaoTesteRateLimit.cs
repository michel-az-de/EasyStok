namespace EasyStock.Api.Configuration;

/// <summary>Rate limit do botão Testar das integrações (F16, #1246).</summary>
public static class IntegracaoTesteRateLimit
{
    public const string Politica = "integracao-teste";
    public const int PorMinuto = 6;

    /// <summary>Partição por loja (claim <c>empresaId</c>) e provider (rota). Sem claim, pelo IP.</summary>
    public static string Chave(HttpContext context)
    {
        var empresa = context.User.FindFirst("empresaId")?.Value;
        var provider = context.Request.RouteValues.TryGetValue("provider", out var valor)
            ? valor?.ToString()?.Trim().ToLowerInvariant()
            : null;
        var dono = string.IsNullOrWhiteSpace(empresa)
            ? "ip:" + (context.Connection.RemoteIpAddress?.ToString() ?? "anon")
            : "empresa:" + empresa;
        return $"{dono}:{provider ?? "-"}";
    }
}
