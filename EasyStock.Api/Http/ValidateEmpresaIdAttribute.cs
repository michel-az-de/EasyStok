using EasyStock.Application.Ports.Output;
using EasyStock.Domain.Enums;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace EasyStock.Api.Http;

/// <summary>
/// Validates that the authenticated user belongs to the requested empresa.
/// Reads "empresaId" from query string; SuperAdmin bypasses the check.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class ValidateEmpresaIdAttribute : TypeFilterAttribute
{
    public ValidateEmpresaIdAttribute() : base(typeof(ValidateEmpresaIdFilter)) { }
}

internal sealed class ValidateEmpresaIdFilter(ICurrentUserAccessor currentUser) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (context.HttpContext.Request.Query.TryGetValue("empresaId", out var raw)
            && Guid.TryParse(raw, out var empresaId)
            && empresaId != Guid.Empty
            && currentUser.Nivel != NivelAcesso.SuperAdmin
            && currentUser.EmpresaId != Guid.Empty
            && currentUser.EmpresaId != empresaId)
        {
            context.Result = new ForbidResult();
            return;
        }

        await next();
    }
}
