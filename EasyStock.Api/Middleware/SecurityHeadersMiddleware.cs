namespace EasyStock.Api.Middleware;

/// <summary>
/// Adiciona headers de segurança padrão em todas as respostas HTTP.
/// </summary>
public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var headers = context.Response.Headers;

        // Remove informacao de tecnologia do servidor (reconnaissance hardening)
        headers.Remove("Server");
        headers.Remove("X-Powered-By");
        headers.Remove("X-AspNet-Version");
        headers.Remove("X-AspNetMvc-Version");

        // Impede que browsers tentem inferir o Content-Type (MIME sniffing)
        headers["X-Content-Type-Options"] = "nosniff";

        // Impede que a pagina seja carregada em iframe (clickjacking)
        headers["X-Frame-Options"] = "DENY";

        // Desativa o filtro XSS legado dos browsers (hoje nao recomendado, melhor desativar)
        headers["X-XSS-Protection"] = "0";

        // Controla quais informacoes de referrer sao enviadas
        headers["Referrer-Policy"] = "strict-origin-when-cross-origin";

        // Restringe acesso a APIs de dispositivo que nao sao necessarias
        headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=()";

        // HSTS: forca HTTPS por 1 ano em todos os subdomains. So aplica quando o request veio via HTTPS
        // (em HTTP o header e ignorado pelo browser, mas evitamos ruido em logs/proxies de dev).
        if (context.Request.IsHttps)
        {
            headers["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains";
        }

        // CSP: balanceado entre paginas HTML servidas (Diagnostico) e API JSON.
        // 'unsafe-inline' em script/style necessario enquanto Diagnostico usa inline; tightening futuro com nonces.
        headers["Content-Security-Policy"] =
            "default-src 'self'; " +
            "script-src 'self' 'unsafe-inline' https://cdn.jsdelivr.net; " +
            "style-src 'self' 'unsafe-inline' https://fonts.googleapis.com; " +
            "img-src 'self' data: https:; " +
            "font-src 'self' data: https://fonts.gstatic.com; " +
            "connect-src 'self'; " +
            "frame-ancestors 'none'; " +
            "base-uri 'self'; " +
            "form-action 'self'";

        await next(context);
    }
}
