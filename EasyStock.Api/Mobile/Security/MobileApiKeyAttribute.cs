using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace EasyStock.Api.Mobile.Security;

/// <summary>
/// Exige header <c>X-Mobile-Api-Key</c> com valor igual ao configurado em
/// <c>Mobile:ApiKey</c>. Se a chave não estiver configurada, retorna 503
/// (módulo desabilitado). Sem JWT — o módulo Mobile é de uso pessoal em
/// rede local, API key é defense-in-depth.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class MobileApiKeyAttribute : TypeFilterAttribute
{
    public MobileApiKeyAttribute() : base(typeof(MobileApiKeyFilter)) { }
}

internal sealed class MobileApiKeyFilter(
    IConfiguration configuration,
    ILogger<MobileApiKeyFilter> logger) : IAsyncActionFilter
{
    private const string HeaderName = "X-Mobile-Api-Key";

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var expected = configuration["Mobile:ApiKey"];

        if (string.IsNullOrWhiteSpace(expected))
        {
            logger.LogError(
                "Mobile:ApiKey não configurado. Todos os requests para /api/mobile/* serão rejeitados.");
            context.Result = new ObjectResult(new { error = "Mobile module not configured." })
            {
                StatusCode = StatusCodes.Status503ServiceUnavailable
            };
            return;
        }

        if (!context.HttpContext.Request.Headers.TryGetValue(HeaderName, out var provided)
            || !FixedTimeEquals(provided.ToString(), expected))
        {
            logger.LogWarning(
                "Mobile API request com chave inválida ou ausente. IP={IP} Path={Path}",
                context.HttpContext.Connection.RemoteIpAddress,
                context.HttpContext.Request.Path);
            context.Result = new UnauthorizedResult();
            return;
        }

        await next();
    }

    /// <summary>Comparação em tempo constante para evitar timing attack.</summary>
    private static bool FixedTimeEquals(string a, string b)
    {
        var ba = Encoding.UTF8.GetBytes(a);
        var bb = Encoding.UTF8.GetBytes(b);
        return ba.Length == bb.Length && CryptographicOperations.FixedTimeEquals(ba, bb);
    }
}
