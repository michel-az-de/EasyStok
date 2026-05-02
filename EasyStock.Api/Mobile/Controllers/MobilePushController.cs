using EasyStock.Api.Mobile.Security;
using EasyStock.Infra.Postgre.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Api.Mobile.Controllers;

/// <summary>
/// Registro de token FCM para push notifications.
/// Device pareado envia seu token após inicialização do FCM no app.
/// Backend armazena e usa para enviar push na feature de notificações operacionais.
/// </summary>
[ApiController]
[Route("api/mobile/push")]
public class MobilePushController(EasyStockDbContext db, ILogger<MobilePushController> log) : ControllerBase
{
    public sealed record RegisterTokenRequest(string FcmToken);

    [HttpPost("register")]
    [MobileApiKey]
    public async Task<IActionResult> RegisterToken([FromBody] RegisterTokenRequest req, CancellationToken ct)
    {
        var device = HttpContext.GetMobileDevice();
        if (device is null) return Unauthorized(new { error = "device não pareado" });

        if (string.IsNullOrWhiteSpace(req?.FcmToken))
            return BadRequest(new { error = "fcmToken é obrigatório" });

        await db.Database.ExecuteSqlInterpolatedAsync($@"
            UPDATE mobile_devices
            SET push_token = {req.FcmToken.Trim()}, updated_at = {DateTime.UtcNow}
            WHERE ""Id"" = {device.Id}", ct);

        log.LogInformation("Push token registrado para device {DeviceId} empresa {EmpresaId}",
            device.Id, device.EmpresaId);

        return Ok(new { registered = true });
    }

    [HttpDelete("unregister")]
    [MobileApiKey]
    public async Task<IActionResult> UnregisterToken(CancellationToken ct)
    {
        var device = HttpContext.GetMobileDevice();
        if (device is null) return Unauthorized(new { error = "device não pareado" });

        await db.Database.ExecuteSqlInterpolatedAsync($@"
            UPDATE mobile_devices
            SET push_token = NULL, updated_at = {DateTime.UtcNow}
            WHERE ""Id"" = {device.Id}", ct);

        return Ok(new { unregistered = true });
    }
}
