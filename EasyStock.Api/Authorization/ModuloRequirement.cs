using EasyStock.Domain.Services;

namespace EasyStock.Api.Authorization;

public sealed record ModuloRequirement(params Modulo[] Modulos) : IAuthorizationRequirement
{
    public bool AceitaBridge { get; init; }
}

public sealed class ModuloAuthorizationHandler(ICurrentUserAccessor usuario) : AuthorizationHandler<ModuloRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, ModuloRequirement requirement)
    {
        if (requirement.AceitaBridge && context.User.Identities.Any(i => i.IsAuthenticated
            && i.AuthenticationType == ImpressaoApiKeyAuthHandler.SchemeName) && usuario.EmpresaId != Guid.Empty)
        {
            context.Succeed(requirement);
            return Task.CompletedTask;
        }
        if (usuario.IsAuthenticated && usuario.EmpresaId != Guid.Empty
            && requirement.Modulos.Any(m => usuario.TemPermissao(AcessoModulos.PermissaoDe(m))))
            context.Succeed(requirement);
        return Task.CompletedTask;
    }
}
