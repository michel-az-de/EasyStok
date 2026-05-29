using System.Collections.Concurrent;

namespace EasyStock.Web.Services;

public sealed class LucideIconResolver
{
    private readonly string _iconsDir;
    private readonly ConcurrentDictionary<string, string> _cache = new();
    private readonly ILogger<LucideIconResolver> _logger;

    public LucideIconResolver(IWebHostEnvironment env, ILogger<LucideIconResolver> logger)
    {
        _iconsDir = Path.Combine(env.WebRootPath, "lib", "lucide", "icons");
        _logger = logger;
    }

    public bool Exists(string name) => TryLoad(name, out _);

    public string? GetSvg(string name)
    {
        return TryLoad(name, out var svg) ? svg : null;
    }

    private bool TryLoad(string name, out string svg)
    {
        svg = string.Empty;
        if (!IsValidName(name)) return false;

        if (_cache.TryGetValue(name, out var cached))
        {
            svg = cached;
            return true;
        }

        var path = Path.Combine(_iconsDir, name + ".svg");
        if (!File.Exists(path))
        {
            _logger.LogWarning("Lucide icon not found: {Name}", name);
            return false;
        }

        try
        {
            var content = File.ReadAllText(path).Trim();
            _cache[name] = content;
            svg = content;
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read Lucide icon: {Name}", name);
            return false;
        }
    }

    private static bool IsValidName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        foreach (var c in name)
        {
            var ok = (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '-';
            if (!ok) return false;
        }
        return true;
    }
}
