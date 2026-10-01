using EasyStock.Application.Ports.Output.Persistence.Storefront;
using Microsoft.AspNetCore.Mvc.Filters;

namespace EasyStock.Api.Http;

/// <summary>
/// Fixa o tenant da requisição anônima do site pelo <c>{slug}</c> da rota (#1259).
/// <para>
/// A rota <c>api/storefront/{slug}/...</c> não tem JWT: sem isto o <c>CurrentTenantId</c> fica
/// <see cref="Guid.Empty"/>, o filtro global do EF e a policy RLS zeram toda leitura e o login por
/// OTP, a sessão do cliente, "meus pedidos" e a avaliação nunca encontram o que acabou de ser
/// gravado. É o mesmo mecanismo que o checkout e o webhook do WhatsApp já usam
/// (<see cref="ITenantContextAccessor.SetCurrentTenant"/>), aplicado antes da action.
/// </para>
/// <para>
/// Usuário autenticado por JWT mantém o tenant da claim: o slug público nunca troca o tenant de
/// quem está logado. Slug inexistente segue sem tenant e o use case devolve 404.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class TenantDoStorefrontAttribute : TypeFilterAttribute
{
    public TenantDoStorefrontAttribute() : base(typeof(TenantDoStorefrontFilter)) { }
}

internal sealed class TenantDoStorefrontFilter(
    IStorefrontRepository storefrontRepository,
    ITenantContextAccessor tenantContext) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (context.HttpContext.User.Identity?.IsAuthenticated != true
            && context.RouteData.Values.TryGetValue("slug", out var valor)
            && valor is string slug
            && !string.IsNullOrWhiteSpace(slug))
        {
            var storefront = await storefrontRepository.GetBySlugAsync(slug, context.HttpContext.RequestAborted);
            if (storefront is not null)
                tenantContext.SetCurrentTenant(storefront.EmpresaId);
        }

        await next();
    }
}
