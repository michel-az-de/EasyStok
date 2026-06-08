using System.Text.RegularExpressions;
using FluentAssertions;

namespace EasyStock.ArchitectureTests;

/// <summary>
/// Guard anti-regressao de paleta: a familia indigo legacy NAO pode reaparecer no
/// EasyStock.Web. O refactor e4de5be0 ("migra paleta indigo->navy + orange accent")
/// removeu o indigo do CSS via sed e mapeou indigo->navy no tailwind.config.js, mas
/// resíduos escaparam em gradientes, CSS de print/mobile e cores hardcoded em JS
/// (charts, toast) — fora do alcance do sed e dos arch-tests de hex (que so olham
/// .cshtml/.css generico, nao JS, e nao distinguem indigo de navy).
///
/// Politica: nenhum hex/rgba da familia indigo em wwwroot (css/js) ou Views. Cores de
/// acao/estrutura usam navy (var(--navy-*)); acento usa laranja. tailwind.dist.css e
/// EXIMIDO (gerado; mapeia indigo->navy, nao e autorado). Os arquivos de DEBITO atuais
/// ficam numa allowlist ratchet-able (igual CssHexHygieneTests/ADR-0024): ao limpar o
/// indigo de um arquivo, remova-o da lista (o teste falha se um arquivo allowlistado
/// nao tem mais indigo, forcando a limpeza). O debito so diminui; indigo novo e barrado.
///
/// Escopo da promocao do laranja (epic #534): a allowlist encolhe ao longo das Fases 1/3.
/// </summary>
[Trait("Category", "Architecture")]
public class IndigoBanTests
{
    // Familia indigo legacy: hexes que o e4de5be0 substituiu + variantes em uso medidas
    // (2026-06-08). rgba(99,102,241) e o indigo-500 do Tailwind em charts JS.
    private static readonly Regex IndigoRegex = new(
        @"#(?:6366[fF]1|818[cC][fF]8|4[fF]46[eE]5|4338[cC][aA]|3730[aA]3|312[eE]81|[aA]5[bB]4[fF][cC]|6[dD]28[dD]9|4[cC]1[dD]95)\b" +
        @"|rgba?\(\s*99\s*,\s*102\s*,\s*241",
        RegexOptions.Compiled);

    /// <summary>Gerado (mapeia indigo->navy). Nunca flagueado.</summary>
    private static readonly HashSet<string> Exempt = new(StringComparer.OrdinalIgnoreCase)
    {
        "tailwind.dist.css",
    };

    /// <summary>
    /// Arquivos com indigo legacy ainda nao limpo (debito documentado, epic #534).
    /// Caminhos relativos a EasyStock.Web/, forward slash. Ao limpar, remova a entrada.
    /// </summary>
    private static readonly HashSet<string> DebtAllowlist = new(StringComparer.OrdinalIgnoreCase)
    {
        "wwwroot/js/toast.js",            // info bg #4F46E5 -> navy (Fase 1)
        "wwwroot/js/dashboard-charts.js", // barras rgba(99,102,241) -> navy (Fase 1/3)
        "wwwroot/css/app.css",            // gradientes do app-primary-btn dark + cores (Fase 1)
        "wwwroot/css/mobile.css",         // bottom-nav-item--active #4338CA (Fase 1/3)
        "wwwroot/css/lista-print.css",    // .lp-btn-primary #4f46e5 (Fase 3)
        "wwwroot/css/components.css",     // overrides dark .bg-indigo-50 / .ring-indigo-200 (Fase 1/2)
    };

    private static readonly string[] ScanGlobs = { "*.css", "*.js", "*.cshtml" };

    [Fact]
    public void Web_NaoDeveConterIndigoLegacy_ForaDaAllowlist()
    {
        var webRoot = LocateWebRoot();
        var offenders = new List<string>();
        foreach (var path in EnumerateScannedFiles(webRoot))
        {
            var name = Path.GetFileName(path);
            var rel = ToRelativeWebPath(webRoot, path);
            if (Exempt.Contains(name) || DebtAllowlist.Contains(rel)) continue;
            if (IndigoRegex.IsMatch(File.ReadAllText(path)))
                offenders.Add(rel);
        }

        offenders.Should().BeEmpty(
            "a familia indigo legacy nao pode reaparecer (decisao e4de5be0: indigo->navy). " +
            "Use var(--navy-*) p/ acao/estrutura e laranja so p/ acento. Se for debito " +
            "transitorio, adicione o arquivo a DebtAllowlist apontando a fase do epic #534.");
    }

    [Fact]
    public void DebtAllowlist_NaoDeveConterArquivosJaLimpos()
    {
        var webRoot = LocateWebRoot();
        var stale = new List<string>();
        foreach (var entry in DebtAllowlist)
        {
            var fullPath = Path.Combine(webRoot.FullName, entry.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(fullPath))
                stale.Add($"{entry} (arquivo nao existe)");
            else if (!IndigoRegex.IsMatch(File.ReadAllText(fullPath)))
                stale.Add($"{entry} (sem indigo - limpo; remova da allowlist)");
        }

        stale.Should().BeEmpty(
            "DebtAllowlist deve refletir o estado real; remova arquivos ja limpos de indigo " +
            "ou inexistentes (o debito so diminui).");
    }

    private static IEnumerable<string> EnumerateScannedFiles(DirectoryInfo webRoot)
    {
        var wwwroot = new DirectoryInfo(Path.Combine(webRoot.FullName, "wwwroot"));
        var views = new DirectoryInfo(Path.Combine(webRoot.FullName, "Views"));
        foreach (var dir in new[] { wwwroot, views })
        {
            if (!dir.Exists) continue;
            foreach (var glob in ScanGlobs)
                foreach (var f in Directory.GetFiles(dir.FullName, glob, SearchOption.AllDirectories))
                    yield return f;
        }
    }

    private static DirectoryInfo LocateWebRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !current.GetFiles("*.sln").Any())
            current = current.Parent;
        if (current is null)
            throw new InvalidOperationException("Nao foi possivel localizar a raiz da solucao.");

        var web = new DirectoryInfo(Path.Combine(current.FullName, "EasyStock.Web"));
        if (!web.Exists)
            throw new InvalidOperationException($"EasyStock.Web nao encontrado em {web.FullName}");
        return web;
    }

    private static string ToRelativeWebPath(DirectoryInfo webRoot, string fullPath) =>
        Path.GetRelativePath(webRoot.FullName, fullPath).Replace(Path.DirectorySeparatorChar, '/');
}
