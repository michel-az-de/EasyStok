namespace EasyStock.Api.Authorization;

/// <summary>
/// Exige uma permissão fina pelo mesmo cálculo do <see cref="ICurrentUserAccessor.TemPermissao"/>
/// (perfil explícito ou fallback do nível). Usado pelas policies de permissão (#1508).
/// </summary>
public sealed record PermissaoRequirement(Permissao Permissao) : IAuthorizationRequirement;

public sealed class PermissaoAuthorizationHandler(ICurrentUserAccessor usuario) : AuthorizationHandler<PermissaoRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissaoRequirement requirement)
    {
        if (usuario.IsAuthenticated && usuario.TemPermissao(requirement.Permissao))
            context.Succeed(requirement);
        return Task.CompletedTask;
    }
}

/// <summary>Nomes das policies de permissão registradas em <c>AddEasyStockAuth</c>.</summary>
public static class PoliticasPermissao
{
    public const string VisualizarRelatorios = nameof(Permissao.VisualizarRelatorios);
}
