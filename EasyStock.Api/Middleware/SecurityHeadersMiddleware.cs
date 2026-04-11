namespace EasyStock.Api.Middleware;

/// <summary>
/// Adiciona headers de segurança padrão em todas as respostas HTTP.
/// </summary>
public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var headers = context.Response.Headers;

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

        await next(context);
    }
}
