using System.Text.RegularExpressions;

namespace EasyStock.Api.Mobile.Services;

/// <summary>
/// Fonte da verdade da versão atual do bundle PWA.
///
/// Lê a constante <c>CACHE_VERSION = '...'</c> do arquivo
/// <c>wwwroot/pwa/sw.js</c> em runtime e mantém em cache. O <c>/api/mobile/version</c>
/// usa esse valor pra reportar a versão real ao PWA, eliminando drift entre
/// o que o servidor anuncia e o que o service worker realmente carrega.
///
/// Evita o problema histórico do <c>Mobile:PwaCacheVersion</c> hardcoded em
/// appsettings: se o sw.js sofre bump mas o config esquece, o auto-update
/// nunca dispara.
///
/// Cache de 60s — re-lê do disco se o tempo expirou. Fallback: config + literal.
/// </summary>
public interface IPwaVersionProvider
{
    /// <summary>Versão atual do CACHE_VERSION lida do sw.js (ou fallback).</summary>
    string GetCurrentCacheVersion();
}

public sealed class PwaVersionProvider(
    IWebHostEnvironment env,
    IConfiguration configuration,
    ILogger<PwaVersionProvider> log) : IPwaVersionProvider
{
    private static readonly Regex CacheVersionRegex = new(
        @"const\s+CACHE_VERSION\s*=\s*['""]([^'""]+)['""]",
        RegexOptions.Compiled);

    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(60);
    private readonly object _gate = new();

    private string? _cached;
    private DateTime _cachedAt;

    public string GetCurrentCacheVersion()
    {
        lock (_gate)
        {
            if (_cached is not null && DateTime.UtcNow - _cachedAt < CacheTtl)
                return _cached;

            var fromFile = TryReadFromSwJs();
            var resolved = fromFile
                ?? configuration["Mobile:PwaCacheVersion"]
                ?? "cdb-unknown";

            if (fromFile is null)
            {
                log.LogWarning("PwaVersionProvider: não consegui ler CACHE_VERSION de sw.js — usando fallback {Value}", resolved);
            }

            _cached = resolved;
            _cachedAt = DateTime.UtcNow;
            return resolved;
        }
    }

    private string? TryReadFromSwJs()
    {
        try
        {
            var webRoot = env.WebRootPath;
            if (string.IsNullOrEmpty(webRoot))
                webRoot = Path.Combine(env.ContentRootPath, "wwwroot");

            var swPath = Path.Combine(webRoot, "pwa", "sw.js");
            if (!File.Exists(swPath)) return null;

            var content = File.ReadAllText(swPath);
            var match = CacheVersionRegex.Match(content);
            return match.Success ? match.Groups[1].Value : null;
        }
        catch (Exception ex)
        {
            log.LogDebug(ex, "PwaVersionProvider: leitura de sw.js falhou");
            return null;
        }
    }
}
