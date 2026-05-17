using EasyStock.Application.Ports.Output.Caching;
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
///
/// <para>
/// O snapshot da assinatura ativa e cacheado por <c>empresaId</c> via
/// <see cref="ISubscriptionStatusCache"/> (TTL 60s). Sem cache, cada
/// request autenticada batia uma query no Postgres — em escala isso vira
/// latencia cumulativa e custo de DB. Invalidacao automatica e disparada
/// pelo <c>AssinaturaCacheInvalidationInterceptor</c> apos qualquer
/// SaveChanges que altere <c>AssinaturaEmpresa</c>.
/// </para>
/// </summary>
public sealed class SubscriptionGateMiddleware(RequestDelegate next, ILogger<SubscriptionGateMiddleware> logger)
{
    // Rotas que SEMPRE passam, independente de status da assinatura.
    private static readonly string[] PathPrefixesAllowed =
    {
        "/api/auth",
        "/api/empresas/registrar",
        "/api/empresas/email-disponivel",
        "/api/empresas/cnpj-disponivel",
        "/api/onboarding",
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

    public async Task InvokeAsync(
        HttpContext context,
        IAssinaturaEmpresaRepository assinaturaRepo,
        ISubscriptionStatusCache statusCache)
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

        var snapshot = await statusCache.GetOrFetchAsync(
            empresaId,
            async ct =>
            {
                var assinatura = await assinaturaRepo.GetAtivaAsync(empresaId);
                return assinatura is null
                    ? null
                    : new SubscriptionStatusSnapshot(assinatura.Status, assinatura.TrialFim, assinatura.DataFim);
            },
            context.RequestAborted);

        var now = DateTime.UtcNow;

        string? blockCode = null;
        string? blockMsg = null;

        if (snapshot is null)
        {
            blockCode = "NO_SUBSCRIPTION";
            blockMsg = "Empresa sem assinatura ativa.";
        }
        else if (snapshot.Status == StatusAssinatura.Suspensa)
        {
            blockCode = "SUBSCRIPTION_SUSPENDED";
            blockMsg = "Assinatura suspensa por inadimplência. Realize o pagamento para reativar.";
        }
        else if (snapshot.Status == StatusAssinatura.Cancelada)
        {
            blockCode = "SUBSCRIPTION_CANCELLED";
            blockMsg = "Assinatura cancelada. Crie uma nova para continuar.";
        }
        else if (snapshot.Status == StatusAssinatura.Expirada)
        {
            blockCode = "SUBSCRIPTION_EXPIRED";
            blockMsg = "Assinatura expirada.";
        }
        else if (TrialExpiradoSemPlanoAtivo(snapshot, now))
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
    private static bool TrialExpiradoSemPlanoAtivo(SubscriptionStatusSnapshot snapshot, DateTime now)
    {
        if (!snapshot.TrialFim.HasValue) return false;
        if (snapshot.TrialFim.Value >= now) return false;
        var planoPagoVigente = snapshot.DataFim.HasValue && snapshot.DataFim.Value >= now;
        return !planoPagoVigente;
    }
}
