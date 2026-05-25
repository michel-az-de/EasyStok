using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using EasyStock.Application.Ports.Output;
using EasyStock.Domain.Enums;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Logging;

namespace EasyStock.Api.Http;

/// <summary>
/// Validates that the authenticated user belongs to the requested empresa.
/// Reads <c>empresaId</c> from three locations (in order):
/// <list type="number">
///   <item>Query string <c>?empresaId={guid}</c></item>
///   <item>Action argument named <c>empresaId</c> (direct <see cref="Guid"/>)</item>
///   <item>Property <c>EmpresaId</c> (Guid or Guid?) on any action argument DTO/command
///         via reflection (per-type cache using compiled expression trees)</item>
/// </list>
/// Any <c>EmpresaId != Guid.Empty</c> mismatching the current user (and not SuperAdmin) results in <c>403</c>.
/// Scoped-user short-circuit: if the caller has no empresa bound (e.g., registration flow),
/// the filter is skipped — downstream use cases still enforce their own scope via <c>UseCaseGuards</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class ValidateEmpresaIdAttribute : TypeFilterAttribute
{
    public ValidateEmpresaIdAttribute() : base(typeof(ValidateEmpresaIdFilter)) { }
}

internal sealed class ValidateEmpresaIdFilter(
    ICurrentUserAccessor currentUser,
    ILogger<ValidateEmpresaIdFilter> logger) : IAsyncActionFilter
{
    // Cache per DTO type of the `EmpresaId` property getter (null if the type does not expose one).
    // Compiled expression trees avoid MethodInfo.Invoke overhead on every request.
    private static readonly ConcurrentDictionary<Type, Func<object, Guid?>?> EmpresaIdGetters = new();

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (!IsScopedUser(currentUser))
        {
            await next();
            return;
        }

        foreach (var candidate in CollectCandidates(context))
        {
            if (!candidate.HasValue || candidate.Value == Guid.Empty) continue;
            if (candidate.Value != currentUser.EmpresaId)
            {
                logger.LogWarning(
                    "Cross-tenant access attempt blocked. UserEmpresaId={UserEmpresaId} AttemptedEmpresaId={AttemptedEmpresaId} Path={Path}",
                    currentUser.EmpresaId, candidate.Value, context.HttpContext.Request.Path);
                context.Result = new ForbidResult();
                return;
            }
        }

        await next();
    }

    private static bool IsScopedUser(ICurrentUserAccessor user) =>
        user.Nivel != NivelAcesso.SuperAdmin && user.EmpresaId != Guid.Empty;

    private static IEnumerable<Guid?> CollectCandidates(ActionExecutingContext context)
    {
        // 1) Query string
        if (context.HttpContext.Request.Query.TryGetValue("empresaId", out var raw)
            && Guid.TryParse(raw, out var qsEmpresaId))
        {
            yield return qsEmpresaId;
        }

        // 2) Action arguments — Guid params named "empresaId", and DTO.EmpresaId via reflection
        foreach (var kv in context.ActionArguments)
        {
            if (kv.Value is null) continue;

            if (kv.Value is Guid direct &&
                string.Equals(kv.Key, "empresaId", StringComparison.OrdinalIgnoreCase))
            {
                yield return direct;
                continue;
            }

            var getter = EmpresaIdGetters.GetOrAdd(kv.Value.GetType(), BuildGetter);
            if (getter is not null)
                yield return getter(kv.Value);
        }
    }

    /// <summary>
    /// Builds a compiled delegate that extracts <c>EmpresaId</c> from instances of <paramref name="type"/>.
    /// Supports both <see cref="Guid"/> and <see cref="Nullable{T}"/> (Guid?) properties.
    /// Returns <c>null</c> when the type does not expose a readable <c>EmpresaId</c>.
    /// Uses expression trees for hot-path performance (compiled once per type, amortized via cache).
    /// </summary>
    private static Func<object, Guid?>? BuildGetter(Type type)
    {
        // Skip primitives and framework types that cannot carry tenant id.
        if (type.IsPrimitive || type == typeof(string) || type == typeof(Guid) || type == typeof(Guid?))
            return null;

        var prop = type.GetProperty(
            "EmpresaId",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);

        if (prop is null || !prop.CanRead) return null;

        var propType = prop.PropertyType;
        var isGuid = propType == typeof(Guid);
        var isGuidN = Nullable.GetUnderlyingType(propType) == typeof(Guid);

        if (!isGuid && !isGuidN) return null;

        // Expression tree equivalent to:
        //   (object o) => (Guid?)((TInstance)o).EmpresaId
        var objParam = Expression.Parameter(typeof(object), "o");
        var casted = Expression.Convert(objParam, type);
        var access = Expression.Property(casted, prop);
        var boxed = isGuid
            ? (Expression)Expression.Convert(access, typeof(Guid?))  // Guid -> Guid?
            : access;                                                // already Guid?

        var lambda = Expression.Lambda<Func<object, Guid?>>(boxed, objParam);
        return lambda.Compile();
    }
}
