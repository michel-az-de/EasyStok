using System.Collections.Concurrent;
using System.Reflection;
using EasyStock.Application.Ports.Output;
using EasyStock.Domain.Enums;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace EasyStock.Api.Http;

/// <summary>
/// Validates that the authenticated user belongs to the requested empresa.
/// Reads <c>empresaId</c> from three locations (in order):
/// <list type="number">
///   <item>Query string <c>?empresaId={guid}</c></item>
///   <item>Action argument named <c>empresaId</c> (direct <see cref="Guid"/>)</item>
///   <item>Property <c>EmpresaId</c> on any action argument DTO/command (reflected, per-type cache)</item>
/// </list>
/// Any <c>EmpresaId != Guid.Empty</c> mismatching the current user (and not SuperAdmin) results in <c>403</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class ValidateEmpresaIdAttribute : TypeFilterAttribute
{
    public ValidateEmpresaIdAttribute() : base(typeof(ValidateEmpresaIdFilter)) { }
}

internal sealed class ValidateEmpresaIdFilter(ICurrentUserAccessor currentUser) : IAsyncActionFilter
{
    // Cache per DTO type of the `EmpresaId` property getter (null if the type does not expose one).
    private static readonly ConcurrentDictionary<Type, Func<object, Guid>?> EmpresaIdGetters = new();

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (!IsScopedUser(currentUser))
        {
            await next();
            return;
        }

        foreach (var candidate in CollectCandidates(context))
        {
            if (candidate == Guid.Empty) continue;
            if (candidate != currentUser.EmpresaId)
            {
                context.Result = new ForbidResult();
                return;
            }
        }

        await next();
    }

    private static bool IsScopedUser(ICurrentUserAccessor user) =>
        user.Nivel != NivelAcesso.SuperAdmin && user.EmpresaId != Guid.Empty;

    private static IEnumerable<Guid> CollectCandidates(ActionExecutingContext context)
    {
        // 1) Query string
        if (context.HttpContext.Request.Query.TryGetValue("empresaId", out var raw)
            && Guid.TryParse(raw, out var qsEmpresaId))
        {
            yield return qsEmpresaId;
        }

        // 2) Action arguments — Guid params named empresaId, and DTO.EmpresaId via reflection
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

    private static Func<object, Guid>? BuildGetter(Type type)
    {
        // Skip primitives and framework types that cannot carry tenant id.
        if (type.IsPrimitive || type == typeof(string) || type == typeof(Guid)) return null;

        var prop = type.GetProperty(
            "EmpresaId",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);

        if (prop is null || prop.PropertyType != typeof(Guid) || !prop.CanRead)
            return null;

        // Compile a fast accessor via MethodInfo.CreateDelegate-ish pattern;
        // cast+invoke is acceptable here since the cache hides the cost.
        var getMethod = prop.GetMethod;
        if (getMethod is null) return null;

        return instance => (Guid)getMethod.Invoke(instance, null)!;
    }
}
