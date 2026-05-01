using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace EasyStock.Application.Services;

public class AuditContext
{
    public Guid UsuarioId { get; set; }
    public string? NomeUsuario { get; set; }
    public string? EmailUsuario { get; set; }
    public string? Ip { get; set; }
    public string? UserAgent { get; set; }
}

public interface IAuditContextService
{
    AuditContext ObtenerContextoAuditoria(Guid? usuarioIdOverride = null);
}

public class AuditContextService(IHttpContextAccessor httpContextAccessor) : IAuditContextService
{
    public AuditContext ObtenerContextoAuditoria(Guid? usuarioIdOverride = null)
    {
        var httpContext = httpContextAccessor.HttpContext;

        var usuarioId = usuarioIdOverride ?? ExtrairUsuarioIdDoClaim(httpContext);
        var nomeUsuario = httpContext?.User.FindFirst(ClaimTypes.Name)?.Value;
        var emailUsuario = httpContext?.User.FindFirst(ClaimTypes.Email)?.Value;
        var ip = ExtrairIpDaRequisicao(httpContext);
        var userAgent = httpContext?.Request.Headers.UserAgent.ToString();

        return new AuditContext
        {
            UsuarioId = usuarioId,
            NomeUsuario = nomeUsuario,
            EmailUsuario = emailUsuario,
            Ip = ip,
            UserAgent = userAgent
        };
    }

    private static Guid ExtrairUsuarioIdDoClaim(HttpContext? httpContext)
    {
        if (httpContext?.User is null)
            return Guid.Empty;

        var idClaim = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)
            ?? httpContext.User.FindFirst("sub")
            ?? httpContext.User.FindFirst("id");

        if (idClaim?.Value is { Length: > 0 } idValue &&
            Guid.TryParse(idValue, out var guidId))
        {
            return guidId;
        }

        return Guid.Empty;
    }

    private static string? ExtrairIpDaRequisicao(HttpContext? httpContext)
    {
        if (httpContext is null)
            return null;

        var ip = httpContext.Request.Headers.TryGetValue("X-Forwarded-For", out var forwardedFor)
            ? forwardedFor.ToString().Split(',').First().Trim()
            : httpContext.Connection.RemoteIpAddress?.ToString();

        return ip;
    }
}
