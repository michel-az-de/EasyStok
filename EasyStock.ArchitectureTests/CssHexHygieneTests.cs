using System.Text.RegularExpressions;
using FluentAssertions;

namespace EasyStock.ArchitectureTests;

/// <summary>
/// Guard de hex hardcoded em .css do EasyStock.Web (Fase 0-core, E1). Complementa o
/// RazorViewHygieneTests (que cobre .cshtml) — o .css estava descoberto.
///
/// Politica: cor hex deve viver SO no tokens.css (fonte da palette). Os demais .css
/// devem usar var(--token). tokens.css e EXIMIDO (definir a palette em hex e o seu
/// papel). Os arquivos de DEBITO atuais (app.css etc.) ficam numa allowlist que torna
/// o debito visivel + ratchet-able: ao migrar um arquivo p/ tokens, remova-o da lista
/// (o teste falha se um arquivo allowlistado nao tem mais hex, forcando a limpeza).
///
/// Assim, um .css NOVO nao pode introduzir hex sem decisao consciente, e o debito so
/// diminui. (Escopo Web por ora, espelhando o foco do RazorViewHygieneTests; o .css do
/// Admin pode entrar numa extensao futura.)
/// </summary>
[Trait("Category", "Architecture")]
public class CssHexHygieneTests
{
    private static readonly Regex HexColorRegex = new(
        @"#[0-9a-fA-F]{6}\b|#[0-9a-fA-F]{3}\b",
        RegexOptions.Compiled);

    /// <summary>
    /// Arquivos onde hex e esperado e nunca deve ser flagueado:
    /// - tokens.css: fonte canonica da palette (definir cor em hex e o seu papel).
    /// - tailwind.dist.css: output GERADO pelo Tailwind (hex por design; nao e autorado).
    /// </summary>
    private static readonly HashSet<string> Exempt = new(StringComparer.OrdinalIgnoreCase)
    {
        "tokens.css",
        "tailwind.dist.css",
    };

    /// <summary>
    /// Arquivos .css com hex hardcoded ainda nao migrados p/ tokens (debito documentado,
    /// roadmap Fase 2/3/4/6). Ao migrar um arquivo, remova-o daqui.
    /// </summary>
    private static readonly HashSet<string> DebtAllowlist = new(StringComparer.OrdinalIgnoreCase)
    {
        "app.css",
        "components.css",
        "auth-premium.css",
        "mobile.css",
        "recibo.css",
        "etiqueta-pedido.css",  // print page standalone (~80mm), hex explicito p/ impressao (refs #583)
        "lista-print.css",
        "print.css",
        "site.css",
        "app-shell.css",
    };

    [Fact]
    public void Css_NaoDeveIntroduzirHexHardcoded_ForaDaAllowlist()
    {
        var cssDir = LocateWebCssDirectory();
        var offenders = new List<string>();
        foreach (var path in Directory.GetFiles(cssDir.FullName, "*.css", SearchOption.AllDirectories))
        {
            var name = Path.GetFileName(path);
            if (Exempt.Contains(name) || DebtAllowlist.Contains(name)) continue;
            if (FileContainsHexColor(path))
                offenders.Add(name);
        }

        offenders.Should().BeEmpty(
            "novos .css (ou recem-modificados fora da allowlist) nao podem introduzir cor " +
            "hex hardcoded; use var(--token) do tokens.css. Se for debito transitorio, " +
            "adicione o arquivo a DebtAllowlist apontando a fase do roadmap.");
    }

    [Fact]
    public void DebtAllowlist_NaoDeveConterArquivosJaLimpos()
    {
        var cssDir = LocateWebCssDirectory();
        var stale = new List<string>();
        foreach (var entry in DebtAllowlist)
        {
            var fullPath = Path.Combine(cssDir.FullName, entry);
            if (!File.Exists(fullPath))
                stale.Add($"{entry} (arquivo nao existe)");
            else if (!FileContainsHexColor(fullPath))
                stale.Add($"{entry} (sem hex - migrado; remova da allowlist)");
        }

        stale.Should().BeEmpty(
            "DebtAllowlist deve refletir o estado real; remova arquivos ja migrados a tokens " +
            "ou inexistentes (o debito so diminui).");
    }

    private static bool FileContainsHexColor(string path) =>
        HexColorRegex.IsMatch(File.ReadAllText(path));

    private static DirectoryInfo LocateWebCssDirectory()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !current.GetFiles("*.sln").Any())
            current = current.Parent;
        if (current is null)
            throw new InvalidOperationException("Nao foi possivel localizar a raiz da solucao.");

        var css = new DirectoryInfo(Path.Combine(current.FullName, "EasyStock.Web", "wwwroot", "css"));
        if (!css.Exists)
            throw new InvalidOperationException($"Pasta css nao encontrada em {css.FullName}");
        return css;
    }
}
