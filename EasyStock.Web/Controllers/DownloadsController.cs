using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EasyStock.Web.Controllers;

/// <summary>
/// Página pública de download do APK Casa da Babá (PWA empacotado).
///
/// O binário e o manifest <c>apk-version.json</c> são publicados pelo CI
/// (.github/workflows/deploy-render.yml job build-apk) em
/// <c>wwwroot/downloads/</c> — não precisamos servir via stream aqui, o
/// middleware estático do ASP.NET cuida disso. Esta controller só renderiza
/// a página com botão/QR e expõe um JSON de versão para o boot do PWA.
/// </summary>
[AllowAnonymous]
[Route("downloads")]
public sealed class DownloadsController(IWebHostEnvironment env, ILogger<DownloadsController> logger) : Controller
{
    private readonly IWebHostEnvironment _env = env;
    private readonly ILogger<DownloadsController> _logger = logger;

    [HttpGet("")]
    public IActionResult Index()
    {
        return View(LoadVersion());
    }

    /// <summary>
    /// Redirect 302 → /downloads/easystok-latest.apk.
    /// Mantém o ponteiro estável: clientes guardam apenas /downloads/apk no
    /// favorito ou no QR Code, e o redirect sempre aponta pro APK mais novo.
    /// </summary>
    [HttpGet("apk")]
    public IActionResult Apk()
    {
        var manifest = LoadVersion();
        var url = string.IsNullOrEmpty(manifest.LatestUrl)
            ? "/downloads/easystok-latest.apk"
            : manifest.LatestUrl;
        return Redirect(url);
    }

    /// <summary>
    /// JSON com versão do APK servido. Consumido pelo PWA boot e pelo APK
    /// Capacitor para detectar updates.
    /// </summary>
    [HttpGet("apk/version")]
    [Produces("application/json")]
    public IActionResult Version()
    {
        return Json(LoadVersion());
    }

    /// <summary>
    /// Manifest para o plugin @capgo/capacitor-updater. Capacitor poll-a
    /// periodicamente este endpoint; se a versão retornada for maior que a
    /// instalada, o plugin baixa o APK do <c>url</c>. Formato compatível com
    /// a doc do plugin: <c>{ version, url, sessionKey?, checksum?, message? }</c>.
    /// </summary>
    [HttpGet("apk/manifest")]
    [Produces("application/json")]
    public IActionResult Manifest()
    {
        var info = LoadVersion();
        if (!info.Available)
        {
            return Json(new { message = "No update available" });
        }
        var scheme = Request.Scheme;
        var host = Request.Host.ToString();
        var apkUrl = $"{scheme}://{host}/downloads/{info.FileName}";
        return Json(new
        {
            version = info.Version,
            url = apkUrl,
            // sessionKey / checksum ficam opcionais — adicionar quando o pipeline
            // gerar SHA256 estável (apk release determinístico).
        });
    }

    private ApkVersionInfo LoadVersion()
    {
        var path = Path.Combine(_env.WebRootPath, "downloads", "apk-version.json");
        if (!System.IO.File.Exists(path))
        {
            return new ApkVersionInfo(
                Sha: "dev",
                Version: "0.0.0-dev",
                ReleasedAt: DateTimeOffset.UtcNow,
                SizeBytes: 0,
                FileName: "easystok-latest.apk",
                LatestUrl: "/downloads/easystok-latest.apk",
                Available: false);
        }

        try
        {
            var json = System.IO.File.ReadAllText(path);
            var info = JsonSerializer.Deserialize<ApkVersionInfo>(json, JsonOpts)
                       ?? throw new InvalidOperationException("apk-version.json inválido");
            return info with { Available = true };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao ler apk-version.json em {Path}", path);
            return new ApkVersionInfo("error", "0.0.0", DateTimeOffset.UtcNow, 0, "", "", false);
        }
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };
}

public record ApkVersionInfo(
    string Sha,
    string Version,
    DateTimeOffset ReleasedAt,
    long SizeBytes,
    string FileName,
    string LatestUrl,
    bool Available = false);
