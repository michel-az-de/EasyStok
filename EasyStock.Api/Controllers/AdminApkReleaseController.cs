using System.Security.Cryptography;
using EasyStock.Domain.Entities.Mobile;
using EasyStock.Infra.Postgre.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Api.Controllers;

/// <summary>
/// Recebe upload de novas releases de APK. Chamado pelo workflow
/// build-casadababa-release.yml depois do build gradlew. Calcula sha256,
/// despromove releases anteriores do mesmo (AppId, IsCanaryOnly), persiste
/// o blob em apk_releases.
///
/// Autenticacao: SuperAdmin policy. CI usa um usuario tecnico SuperAdmin
/// pra invocar via Bearer token (login normal /api/auth/login).
///
/// Limite de payload: 50MB (configurar em Program.cs se necessario com
/// MaxRequestBodySize). APKs Casa da Baba release ficam ~15-25MB.
/// </summary>
[ApiController]
[Route("api/admin/apk-release")]
[Authorize(Policy = "SuperAdmin")]
public class AdminApkReleaseController(
    EasyStockDbContext db,
    ILogger<AdminApkReleaseController> log) : ControllerBase
{
    private const long MaxApkBytes = 50L * 1024 * 1024;

    [HttpPost]
    [RequestSizeLimit(MaxApkBytes)]
    public async Task<IActionResult> Upload(
        IFormFile apk,
        [FromForm] string version,
        [FromForm] string? appId,
        [FromForm] string? releaseNotes,
        [FromForm] bool isCanaryOnly,
        CancellationToken ct)
    {
        if (apk == null || apk.Length == 0)
            return BadRequest(new { error = "arquivo apk obrigatorio" });
        if (apk.Length > MaxApkBytes)
            return BadRequest(new { error = $"apk excede limite de {MaxApkBytes / 1024 / 1024}MB" });
        if (string.IsNullOrWhiteSpace(version))
            return BadRequest(new { error = "version obrigatoria" });

        appId ??= "casa-da-baba";

        // Le bytes + calcula sha256 numa unica passada
        byte[] content;
        string sha256;
        using (var ms = new MemoryStream())
        {
            await apk.CopyToAsync(ms, ct);
            content = ms.ToArray();
            sha256 = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
        }

        // Despromove releases anteriores do mesmo (AppId, IsCanaryOnly).
        // Mantem registro historico — so vira IsActive=false.
        await db.ApkReleases
            .Where(r => r.AppId == appId && r.IsCanaryOnly == isCanaryOnly && r.IsActive)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.IsActive, false), ct);

        var release = new ApkRelease
        {
            Id = Guid.NewGuid(),
            AppId = appId,
            Version = version.Trim(),
            Sha256 = sha256,
            ReleaseNotes = releaseNotes,
            FileContent = content,
            FileSizeBytes = content.LongLength,
            IsCanaryOnly = isCanaryOnly,
            IsActive = true,
            CriadoEm = DateTime.UtcNow
        };
        db.ApkReleases.Add(release);
        await db.SaveChangesAsync(ct);

        log.LogInformation("APK release {Id} v{Version} ({Size} bytes, canary={Canary}) publicada",
            release.Id, release.Version, release.FileSizeBytes, release.IsCanaryOnly);

        return StatusCode(201, new
        {
            id = release.Id,
            version = release.Version,
            sha256 = release.Sha256,
            sizeBytes = release.FileSizeBytes,
            isCanaryOnly = release.IsCanaryOnly,
            manifestUrl = $"/api/mobile/apk/manifest?appId={appId}",
            downloadUrl = $"/api/mobile/apk/download/{release.Id}"
        });
    }

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string? appId,
        [FromQuery] int limit = 20,
        CancellationToken ct = default)
    {
        appId ??= "casa-da-baba";
        var clamped = Math.Clamp(limit, 1, 100);

        var releases = await db.ApkReleases
            .AsNoTracking()
            .Where(r => r.AppId == appId)
            .OrderByDescending(r => r.CriadoEm)
            .Take(clamped)
            .Select(r => new
            {
                r.Id,
                r.Version,
                r.Sha256,
                r.ReleaseNotes,
                r.FileSizeBytes,
                r.IsCanaryOnly,
                r.IsActive,
                r.CriadoEm
            })
            .ToListAsync(ct);

        return Ok(releases);
    }
}
