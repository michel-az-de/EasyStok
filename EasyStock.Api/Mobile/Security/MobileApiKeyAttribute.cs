using System.Security.Cryptography;
using System.Text;
using EasyStock.Application.UseCases.Common;
using EasyStock.Domain.Entities.Mobile;
using EasyStock.Infra.Postgre.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Api.Mobile.Security;

/// <summary>
/// Header sempre lido pelo middleware: <c>X-Mobile-Api-Key</c>.
/// </summary>
public static class MobileAuth
{
    public const string HeaderName = "X-Mobile-Api-Key";
    public const string HttpContextItemDevice = "mobile-device";
}

/// <summary>
/// Filtro de auth do módulo Mobile (Onda 1).
///
/// Comportamento depende da flag <c>Mobile:RequireApiKey</c> (default false):
///
/// <list type="bullet">
///   <item><b>false</b> (modo de transição): se header presente, resolve o
///         device e popula HttpContext; se ausente ou inválido, deixa passar
///         sem device (compatível com APKs pré-Onda-1).</item>
///   <item><b>true</b> (modo final): exige header válido, retorna 401 se
///         ausente/inválido/revogado.</item>
/// </list>
///
/// Ao resolver um device válido, atualiza <c>last_seen_at</c> e
/// <c>last_seen_ip</c> de forma assíncrona (fire-and-forget) e expõe o
/// device em <c>HttpContext.Items["mobile-device"]</c> pro controller.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class MobileApiKeyAttribute : TypeFilterAttribute
{
    public MobileApiKeyAttribute() : base(typeof(MobileApiKeyFilter)) { }
}

internal sealed class MobileApiKeyFilter(
    EasyStockDbContext db,
    IConfiguration configuration,
    ILogger<MobileApiKeyFilter> logger) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var requireApiKey = configuration.GetValue<bool>("Mobile:RequireApiKey", false);
        var http = context.HttpContext;

        if (!http.Request.Headers.TryGetValue(MobileAuth.HeaderName, out var provided)
            || string.IsNullOrWhiteSpace(provided.ToString()))
        {
            // Sem header. Se a flag de obrigatoriedade está ligada, recusa.
            if (requireApiKey)
            {
                logger.LogWarning("Mobile request sem X-Mobile-Api-Key. Path={Path} IP={IP}",
                    http.Request.Path, http.Connection.RemoteIpAddress);
                context.Result = new UnauthorizedResult();
                return;
            }
            // Caso contrário deixa passar sem device — compat legado.
            await next();
            return;
        }

        var apiKey = provided.ToString().Trim();
        var apiKeyHash = TokenHashHelper.ComputeSha256Hash(apiKey);

        // Lookup pelo hash (indexed unique). DB nunca vê plaintext da key.
        var device = await db.Set<MobileDevice>()
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.ApiKeyHash == apiKeyHash);

        if (device == null || device.Revoked)
        {
            logger.LogWarning("Mobile API key inválida ou revogada. Path={Path} IP={IP} Revoked={Revoked}",
                http.Request.Path,
                http.Connection.RemoteIpAddress,
                device?.Revoked ?? false);
            context.Result = new UnauthorizedResult();
            return;
        }

        // Defesa contra timing attack — comparação em tempo constante do hash.
        if (!FixedTimeEquals(device.ApiKeyHash, apiKeyHash))
        {
            context.Result = new UnauthorizedResult();
            return;
        }

        // Disponibiliza o device pro controller via HttpContext.Items.
        http.Items[MobileAuth.HttpContextItemDevice] = device;

        // Atualiza last_seen async (fire-and-forget). Não espera nem
        // bloqueia o request — perda eventual em crash não é crítico.
        _ = Task.Run(async () =>
        {
            try
            {
                await db.Database.ExecuteSqlInterpolatedAsync($@"
                    UPDATE mobile_devices
                    SET last_seen_at = {DateTime.UtcNow},
                        last_seen_ip = {http.Connection.RemoteIpAddress?.ToString() ?? ""}
                    WHERE ""Id"" = {device.Id}");
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Falha ao atualizar last_seen_at do device {DeviceId}", device.Id);
            }
        });

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

/// <summary>Helpers pra recuperar o device autenticado num controller.</summary>
public static class MobileDeviceContextExtensions
{
    /// <summary>Retorna o device autenticado (ou null se anonymous/legado).</summary>
    public static MobileDevice? GetMobileDevice(this HttpContext http)
        => http.Items.TryGetValue(MobileAuth.HttpContextItemDevice, out var v)
            ? v as MobileDevice
            : null;
}
