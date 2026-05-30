using System.Security.Cryptography;

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
    IApkReleaseRepository apkReleases,
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
        // Valida extensao e content-type para nao servir arquivos arbitrarios via download.
        var ext = Path.GetExtension(apk.FileName ?? "").ToLowerInvariant();
        if (ext != ".apk" && apk.ContentType != "application/vnd.android.package-archive")
            return BadRequest(new { error = "arquivo deve ser um .apk valido" });
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

        var criada = await apkReleases.PublicarAsync(
            new ApkReleaseNova(appId, version.Trim(), sha256, releaseNotes, content, isCanaryOnly), ct);

        log.LogInformation("APK release {Id} v{Version} ({Size} bytes, canary={Canary}) publicada",
            criada.Id, criada.Version, criada.FileSizeBytes, criada.IsCanaryOnly);

        return StatusCode(201, new
        {
            id = criada.Id,
            version = criada.Version,
            sha256 = criada.Sha256,
            sizeBytes = criada.FileSizeBytes,
            isCanaryOnly = criada.IsCanaryOnly,
            manifestUrl = $"/api/mobile/apk/manifest?appId={appId}",
            downloadUrl = $"/api/mobile/apk/download/{criada.Id}"
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

        var releases = await apkReleases.ListarAsync(appId, clamped, ct);

        return Ok(releases);
    }
}
