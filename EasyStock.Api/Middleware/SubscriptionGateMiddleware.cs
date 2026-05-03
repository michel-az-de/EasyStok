using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Enums;
using System.Security.Claims;
using System.Text.Json;

namespace EasyStock.Api.Middleware;

/// <summary>
/// Bloqueia (com 402) requisições de tenants suspensos ou com trial expirado.
///
/// Regras:
///   - Sem JWT (anônimo): segue (controllers decidem com [Authorize]/[AllowAnonymous]).
///   - SuperAdmin: sempre passa (precisa acessar painel admin pra resolver inadimplência).
///   - Rotas explicitamente liberadas: pagamentos, login, logout, health, webhooks, swagger.
///   - StatusAssinatura.Suspensa, Cancelada, Expirada → 402 PAYMENT_REQUIRED.
///   - Trial expirado (TrialFim &lt; UtcNow e DataFim ainda não setado) → 402 TRIAL_EXPIRED.
///
/// Sem assinatura ativa = bloqueia. Mantém o sistema honesto: cliente
/// inadimplente não deve usar features.
/// </summary>
public sealed class SubscriptionGateMiddleware(RequestDelegate next, ILogger<SubscriptionGateMiddleware> logger)
{
    // Rotas que SEMPRE passam, independente de status da assinatura.
    private static readonly string[] PathPrefixesAllowed =
    {
        "/api/auth",
        "/api/empresas/registrar",
        "/api/assinatura",
        "/api/webhooks",
        "/api/admin",
        "/api/ia/uso",
        "/api/diagnostico",
        "/health",
        "/swagger",
        "/api/mobile/version",
        "/api/mobile/devices",
    };

    public async Task InvokeAsync(HttpContext context, IAssinaturaEmpresaRepository assinaturaRepo)
    {
        var path = context.Request.Path.Value ?? "";
        if (PathPrefixesAllowed.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
        {
            await next(context);
            return;
        }

        var user = context.User;
        if (user?.Identity?.IsAuthenticated != true)
        {
            await next(context);
            return;
        }

        // SuperAdmin não tem EmpresaId — segue direto.
        var nivel = user.FindFirstValue("nivel");
        if (string.Equals(nivel, "SuperAdmin", StringComparison.OrdinalIgnoreCase))
        {
            await next(context);
            return;
        }

        var empresaIdClaim = user.FindFirstValue("empresaId");
        if (!Guid.TryParse(empresaIdClaim, out var empresaId))
        {
            await next(context);
            return;
        }

        var assinatura = await assinaturaRepo.GetAtivaAsync(empresaId);
        var now = DateTime.UtcNow;

        string? blockCode = null;
        string? blockMsg = null;

        if (assinatura is null)
        {
            blockCode = "NO_SUBSCRIPTION";
            blockMsg = "Empresa sem assinatura ativa.";
        }
        else if (assinatura.Status == StatusAssinatura.Suspensa)
        {
            blockCode = "SUBSCRIPTION_SUSPENDED";
            blockMsg = "Assinatura suspensa por inadimplência. Realize o pagamento para reativar.";
        }
        else if (assinatura.Status == StatusAssinatura.Cancelada)
        {
            blockCode = "SUBSCRIPTION_CANCELLED";
            blockMsg = "Assinatura cancelada. Crie uma nova para continuar.";
        }
        else if (assinatura.Status == StatusAssinatura.Expirada)
        {
            blockCode = "SUBSCRIPTION_EXPIRED";
            blockMsg = "Assinatura expirada.";
        }
        else if (TrialExpiradoSemPlanoAtivo(assinatura, now))
        {
            blockCode = "TRIAL_EXPIRED";
            blockMsg = "Período de teste expirado. Faça upgrade para continuar.";
        }

        if (blockCode is null)
        {
            await next(context);
            return;
        }

        logger.LogInformation("Bloqueando {Path} para EmpresaId {EmpresaId}: {Code}", path, empresaId, blockCode);

        context.Response.StatusCode = StatusCodes.Status402PaymentRequired;
        context.Response.ContentType = "application/json; charset=utf-8";
        var body = JsonSerializer.Serialize(new
        {
            error = new
            {
                code = blockCode,
                message = blockMsg,
                upgradeUrl = "/assinatura"
            }
        });
        await context.Response.WriteAsync(body);
    }

    // Trial expirou E não há plano pago vigente (DataFim null = nunca pagou; DataFim < now = plano expirado).
    // Usuário com plano pago vigente passa mesmo se TrialFim < now.
    private static bool TrialExpiradoSemPlanoAtivo(EasyStock.Domain.Entities.AssinaturaEmpresa assinatura, DateTime now)
    {
        if (!assinatura.TrialFim.HasValue) return false;
        if (assinatura.TrialFim.Value >= now) return false;
        var planoPagoVigente = assinatura.DataFim.HasValue && assinatura.DataFim.Value >= now;
        return !planoPagoVigente;
    }
}
