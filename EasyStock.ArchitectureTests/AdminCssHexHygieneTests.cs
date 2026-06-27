using System.Text.RegularExpressions;
using FluentAssertions;

namespace EasyStock.ArchitectureTests;

/// <summary>
/// Guard de hex hardcoded em .css do EasyStock.Admin — espelho do <see cref="CssHexHygieneTests"/>
/// (Web), estendendo a higiene de design ao Admin (issue #705; linhagem do epico #530 / ADR-0029).
/// O proprio CssHexHygieneTests previa esta extensao ("o .css do Admin pode entrar numa extensao
/// futura"); aqui ela acontece.
///
/// Politica identica a do Web: cor hex deve viver SO no tokens.css (fonte da palette — inclui as
/// definicoes dos tokens --deck-* do "Deck de Operacoes") e no tailwind.dist.css (output gerado).
/// Os demais .css usam var(--token). A DebtAllowlist torna o debito visivel + ratchet-able: ao
/// migrar um arquivo p/ tokens, remova-o (o segundo Fact falha se um arquivo allowlistado nao tem
/// mais hex, forcando a limpeza). Assim um .css NOVO do Admin nao introduz hex sem decisao
/// consciente, e o debito so diminui.
/// </summary>
[Trait("Category", "Architecture")]
public class AdminCssHexHygieneTests
{
    private static readonly Regex HexColorRegex = new(
        @"#[0-9a-fA-F]{6}\b|#[0-9a-fA-F]{3}\b",
        RegexOptions.Compiled);

    /// <summary>
    /// Arquivos onde hex e esperado e nunca deve ser flagueado:
    /// - tokens.css: fonte canonica da palette (define cor em hex, inclusive os tokens --deck-*).
    /// - tailwind.dist.css: output GERADO pelo Tailwind (hex por design; nao e autorado).
    /// </summary>
    private static readonly HashSet<string> Exempt = new(StringComparer.OrdinalIgnoreCase)
    {
        "tokens.css",
        "tailwind.dist.css",
    };

    /// <summary>
    /// .css do Admin com hex hardcoded ainda nao migrado p/ tokens (debito documentado, issue #705).
    /// Ao migrar um arquivo, remova-o daqui.
    /// NOTA: admin-premium.css entra so por causa de 3 referencias de issue (#614/#618/#638) em
    /// COMENTARIO — a regex crua casa hex de 3 digitos (mesmo gotcha do css-hex-hygiene do Web). O
    /// arquivo NAO define cor; quando esses comentarios sairem, o ratchet exigira remove-lo da lista.
    /// </summary>
    private static readonly HashSet<string> DebtAllowlist = new(StringComparer.OrdinalIgnoreCase)
    {
        "admin-premium.css",
        "admin.css",
        "app-shell.css",
        "components.css",
        "notificacoes.css",
    };

    [Fact]
    public void Css_NaoDeveIntroduzirHexHardcoded_ForaDaAllowlist()
    {
        var cssDir = ArchTestPaths.AppDirectory("EasyStock.Admin", "wwwroot", "css");
        var offenders = new List<string>();
        foreach (var path in Directory.GetFiles(cssDir.FullName, "*.css", SearchOption.AllDirectories))
        {
            var name = Path.GetFileName(path);
            if (Exempt.Contains(name) || DebtAllowlist.Contains(name)) continue;
            if (FileContainsHexColor(path))
                offenders.Add(name);
        }

        offenders.Should().BeEmpty(
            "novos .css do Admin (ou recem-modificados fora da allowlist) nao podem introduzir cor " +
            "hex hardcoded; use var(--token) do tokens.css. Se for debito transitorio, adicione o " +
            "arquivo a DebtAllowlist (issue #705).");
    }

    [Fact]
    public void DebtAllowlist_NaoDeveConterArquivosJaLimpos()
    {
        var cssDir = ArchTestPaths.AppDirectory("EasyStock.Admin", "wwwroot", "css");
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
}
