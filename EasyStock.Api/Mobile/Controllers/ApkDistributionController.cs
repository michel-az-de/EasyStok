using EasyStock.Domain.Entities.Mobile;
using EasyStock.Infra.Postgre.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Api.Mobile.Controllers;

/// <summary>
/// Endpoints consumidos pelo CapacitorUpdater plugin no APK Casa da Baba.
///
/// O plugin (configurado em capacitor.config.json) pinga periodicamente
/// /api/mobile/apk/manifest e, se versao diferente da local, baixa o arquivo
/// de /api/mobile/apk/download e aplica o update silenciosamente (ou pergunta
/// ao usuario, conforme directUpdate).
///
/// Canary aware: se X-Device-Id header presente e o device tem IsCanary=true,
/// o manifest retorna a release marcada como canary (se existir). Devices
/// normais recebem a release Active sem canary.
///
/// Kill switch: Ota:Enabled=false faz manifest retornar 204 No Content —
/// CapacitorUpdater interpreta como "nada novo".
/// </summary>
[ApiController]
[Route("api/mobile/apk")]
[AllowAnonymous]
public class ApkDistributionController(
    EasyStockDbContext db,
    IConfiguration configuration,
    ILogger<ApkDistributionController> log) : ControllerBase
{
    private const string DefaultAppId = "casa-da-baba";

    [HttpGet("manifest")]
    public async Task<IActionResult> GetManifest(
        [FromQuery] string? appId,
        CancellationToken ct)
    {
        if (!configuration.GetValue<bool>("Ota:Enabled", true))
            return NoContent();

        appId ??= DefaultAppId;

        var isCanaryDevice = await ResolveCanaryAsync(ct);

        // Procura release Active. Se device canary e existe release canary
        // ativa, retorna ela. Caso contrario retorna a Active nao-canary.
        ApkRelease? release = null;
        if (isCanaryDevice)
        {
            release = await db.ApkReleases
                .AsNoTracking()
                .Where(r => r.AppId == appId && r.IsActive && r.IsCanaryOnly)
                .OrderByDescending(r => r.CriadoEm)
                .FirstOrDefaultAsync(ct);
        }

        release ??= await db.ApkReleases
            .AsNoTracking()
            .Where(r => r.AppId == appId && r.IsActive && !r.IsCanaryOnly)
            .OrderByDescending(r => r.CriadoEm)
            .FirstOrDefaultAsync(ct);

        if (release == null) return NoContent();

        // Formato compativel com CapacitorUpdater plugin (cloud mode self-hosted)
        return Ok(new ApkManifestResponse(
            Version: release.Version,
            Url: BuildDownloadUrl(release.Id),
            Sha256: release.Sha256,
            ReleaseNotes: release.ReleaseNotes,
            IsCanary: release.IsCanaryOnly,
            FileSizeBytes: release.FileSizeBytes
        ));
    }

    [HttpGet("download/{releaseId:guid}")]
    public async Task<IActionResult> Download(Guid releaseId, CancellationToken ct)
    {
        // Plugin valida sha256 ao salvar — nao precisamos re-validar aqui.
        // Mas verificamos IsActive pra nao distribuir release retirada.
        var release = await db.ApkReleases
            .AsNoTracking()
            .Where(r => r.Id == releaseId && r.IsActive)
            .Select(r => new { r.Version, r.FileContent, r.FileSizeBytes })
            .FirstOrDefaultAsync(ct);

        if (release == null) return NotFound();

        log.LogInformation("APK download: release {Id} v{Version} ({Size} bytes)",
            releaseId, release.Version, release.FileSizeBytes);

        return File(release.FileContent, "application/vnd.android.package-archive",
            fileDownloadName: $"casa-da-baba-{release.Version}.apk");
    }

    private async Task<bool> ResolveCanaryAsync(CancellationToken ct)
    {
        if (!Request.Headers.TryGetValue("X-Device-Id", out var headerVal))
            return false;
        var deviceId = headerVal.ToString();
        if (string.IsNullOrWhiteSpace(deviceId) || deviceId.Length > 64)
            return false;

        return await db.Set<MobileDevice>()
            .AsNoTracking()
            .Where(d => d.Id == deviceId && !d.Revoked)
            .Select(d => d.IsCanary)
            .FirstOrDefaultAsync(ct);
    }

    private string BuildDownloadUrl(Guid releaseId)
    {
        // Render (e maioria dos PaaS) termina TLS no load balancer e repassa
        // como HTTP interno. Request.Scheme seria "http" mesmo pra clientes HTTPS.
        // X-Forwarded-Proto tem o scheme real vindo do cliente.
        var scheme = Request.Headers["X-Forwarded-Proto"].FirstOrDefault() ?? Request.Scheme;
        var host = Request.Headers["X-Forwarded-Host"].FirstOrDefault() ?? Request.Host.ToUriComponent();
        return $"{scheme}://{host}/api/mobile/apk/download/{releaseId}";
    }
}

public sealed record ApkManifestResponse(
    string Version,
    string Url,
    string Sha256,
    string? ReleaseNotes,
    bool IsCanary,
    long FileSizeBytes
);
