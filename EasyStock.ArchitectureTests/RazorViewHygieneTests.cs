using System.Text.RegularExpressions;
using FluentAssertions;

namespace EasyStock.ArchitectureTests;

/// <summary>
/// Guarda contra regressao de design system na pasta EasyStock.Web/Views/.
///
/// Bloqueia cores hex hardcoded em arquivos .cshtml. Cores devem usar tokens CSS
/// (var(--orange-600), var(--navy-700), etc) ou classes Tailwind ja mapeadas no
/// tailwind.config.js.
///
/// Lista de excecoes conhecidas (HexAllowlist) representa debito tecnico documentado
/// no plano de refatoracao Fase 2/3/4/6. Ao migrar um arquivo da lista, remova-o.
/// O teste tambem falha se um arquivo da allowlist nao tem mais hex, forcando limpeza.
/// </summary>
[Trait("Category", "Architecture")]
public class RazorViewHygieneTests
{
    private static readonly Regex HexColorRegex = new(
        @"#[0-9a-fA-F]{6}\b|#[0-9a-fA-F]{3}\b",
        RegexOptions.Compiled);

    // Comentarios Razor (@* *@) e HTML (<!-- -->) nao renderizam — hex neles, e
    // referencias de issue tipo "#415" (que casam como hex de 3 digitos), sao
    // falso-positivo. Removidos antes de escanear.
    private static readonly Regex CommentRegex = new(
        @"@\*.*?\*@|<!--.*?-->",
        RegexOptions.Compiled | RegexOptions.Singleline);

    /// <summary>
    /// Arquivos .cshtml com hex hardcoded ainda nao migrados, por fase do roadmap.
    /// Caminhos relativos a EasyStock.Web/Views/. Use forward slash '/'.
    /// </summary>
    private static readonly HashSet<string> HexAllowlist = new(StringComparer.OrdinalIgnoreCase)
    {
        // Fase 1 — gallery do DS (hex em side-by-side panes para forcar tema, intencional)
        "Dev/Components.cshtml",

    };

    [Fact]
    public void Views_NaoDevemConterCoresHexHardcoded_ForaDaAllowlist()
    {
        var viewsDir = LocateViewsDirectory();
        var allCshtml = Directory.GetFiles(viewsDir.FullName, "*.cshtml", SearchOption.AllDirectories);

        var offenders = new List<string>();
        foreach (var path in allCshtml)
        {
            var relative = ToRelativeViewPath(viewsDir, path);
            if (HexAllowlist.Contains(relative)) continue;
            if (FileContainsHexColor(path))
                offenders.Add(relative);
        }

        offenders.Should().BeEmpty(
            "views novas ou recem-modificadas nao podem introduzir cores hex hardcoded; " +
            "use tokens CSS (var(--orange-600), var(--navy-700)) ou classes Tailwind. " +
            "Se for um caso transitorio, adicione o arquivo a HexAllowlist com comentario apontando a fase do roadmap.");
    }

    [Fact]
    public void HexAllowlist_NaoDevemConterArquivosJaLimpos()
    {
        var viewsDir = LocateViewsDirectory();
        var stale = new List<string>();
        foreach (var entry in HexAllowlist)
        {
            var fullPath = Path.Combine(viewsDir.FullName, entry.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(fullPath))
            {
                stale.Add($"{entry} (arquivo nao existe)");
                continue;
            }
            if (!FileContainsHexColor(fullPath))
                stale.Add($"{entry} (sem hex hardcoded - remova da allowlist)");
        }

        stale.Should().BeEmpty(
            "HexAllowlist deve refletir o estado real do codigo; " +
            "remova entradas correspondentes a arquivos ja migrados ou inexistentes.");
    }

    private static bool FileContainsHexColor(string path)
    {
        var content = File.ReadAllText(path);
        content = CommentRegex.Replace(content, " "); // hex em comentario nao renderiza
        foreach (Match match in HexColorRegex.Matches(content))
        {
            var idx = match.Index;
            var preceding = content.AsSpan(Math.Max(0, idx - 8), Math.Min(8, idx)).ToString();

            // Ignora hex que faz parte de href="#anchor" (links internos)
            if (preceding.Contains("href=\"") && match.Value.Length == 4)
                continue;

            // Ignora character entities decimais (&#NNN;) e hex (&#xNNN;)
            if (idx > 0 && content[idx - 1] == '&')
                continue;

            // Hex valido encontrado fora de contexto de anchor/entity
            return true;
        }
        return false;
    }

    private static DirectoryInfo LocateViewsDirectory()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !current.GetFiles("*.sln").Any())
            current = current.Parent;
        if (current is null)
            throw new InvalidOperationException("Nao foi possivel localizar a raiz da solucao.");

        var views = new DirectoryInfo(Path.Combine(current.FullName, "EasyStock.Web", "Views"));
        if (!views.Exists)
            throw new InvalidOperationException($"Pasta Views nao encontrada em {views.FullName}");
        return views;
    }

    private static string ToRelativeViewPath(DirectoryInfo viewsDir, string fullPath)
    {
        var rel = Path.GetRelativePath(viewsDir.FullName, fullPath);
        return rel.Replace(Path.DirectorySeparatorChar, '/');
    }
}
