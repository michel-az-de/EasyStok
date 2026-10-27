using EasyStock.Api.Mobile.DTOs;
using EasyStock.Api.Mobile.Security;
using EasyStock.Api.Mobile.Services;

namespace EasyStock.Api.Mobile.Controllers;

[ApiController]
[AllowAnonymous]
[MobileApiKey]
[Route("api/mobile/legacy-import")]
public class LegacySnapshotController(LegacySnapshotImportService service) : ControllerBase
{
    [HttpPost]
    [RequestSizeLimit(32 * 1024 * 1024)]
    public async Task<IActionResult> Import([FromBody] LegacySnapshotRequest request, CancellationToken ct)
    {
        var device = HttpContext.GetMobileDevice();
        if (device is null) return Unauthorized(new { code = "PAIRING_REQUIRED", error = "Aparelho nao pareado." });
        try { return Ok(await service.ImportAsync(request, device, ct)); }
        catch (LegacyImportException e) { return StatusCode(e.Status, new { code = e.Code, error = e.Message }); }
        catch (ArgumentException) { return BadRequest(new { code = "LEGACY_VALUE_INVALID", error = "Um valor ou data do snapshot e invalido. Preserve o arquivo para revisao." }); }
        catch (System.Text.Json.JsonException) { return BadRequest(new { code = "LEGACY_SNAPSHOT_INVALID", error = "Snapshot JSON invalido." }); }
    }

    [HttpGet("{importId:guid}/snapshot")]
    public async Task<IActionResult> Read(Guid importId, CancellationToken ct)
    {
        var device = HttpContext.GetMobileDevice();
        if (device is null) return Unauthorized();
        var result = await service.ReadAsync(importId, device, ct);
        return result is null ? NotFound() : Ok(result);
    }
}
