using System.Text.RegularExpressions;
using FluentAssertions;

namespace EasyStock.ArchitectureTests;

/// <summary>
/// Guard de coesao de marca em EasyStock.Admin/Pages/ — espelho do
/// <see cref="RazorViewColorUtilityHygieneTests"/> (Web), estendendo a higiene ao Admin (issue #705).
///
/// Bloqueia utilitarios de COR SEMANTICA crus do Tailwind (bg/text/border/ring/divide/from/to/via/
/// fill/stroke - red/amber/yellow/lime/green/emerald/teal/cyan/sky/blue/violet/purple/fuchsia/pink/
/// rose) em .cshtml. Essas cores NAO batem com os tokens de marca (crit/ok/warn/navy) nem com os
/// tokens --deck-* do "Deck de Operacoes". O Admin e dark-first, entao cor semantica crua quebra a
/// identidade visual exatamente onde ela e mais visivel. Cores on-brand devem usar tokens
/// (bg-[var(--crit-100)]) ou as classes de marca do tailwind.config.js (navy/orange/ok/warn/crit/ink).
///
/// FORA DE ESCOPO (proposital, identico ao Web): slate/gray (neutros, remapeados no dark), orange
/// (cor de marca) e indigo (aliasado a navy).
///
/// A DebtAllowlist torna o debito visivel + ratchet-able: ao migrar uma pagina p/ tokens, remova-a
/// (o segundo Fact falha se um arquivo allowlistado nao tem mais cor semantica). Uma pagina NOVA do
/// Admin nao introduz cor off-brand sem decisao consciente.
/// </summary>
[Trait("Category", "Architecture")]
public class AdminRazorViewColorUtilityHygieneTests
{
    private static readonly Regex SemanticColorUtilityRegex = new(
        @"\b(bg|text|border|ring|divide|from|to|via|fill|stroke)-(red|amber|yellow|lime|green|emerald|teal|cyan|sky|blue|violet|purple|fuchsia|pink|rose)-[0-9]",
        RegexOptions.Compiled);

    // Comentarios Razor (@* *@) e HTML (<!-- -->) nao renderizam; cor semantica neles e
    // falso-positivo. Removidos antes de escanear (mesmo padrao do guard do Web).
    private static readonly Regex CommentRegex = new(
        @"@\*.*?\*@|<!--.*?-->",
        RegexOptions.Compiled | RegexOptions.Singleline);

    /// <summary>
    /// Paginas do Admin com cor semantica crua ainda nao migradas a tokens (debito documentado,
    /// issue #705). Caminhos relativos a EasyStock.Admin/Pages/, forward slash '/'. Ao migrar,
    /// remova daqui. Lista medida por forense adversarial (51/51 paginas, regex com strip de
    /// comentario; todas seguem offender pos-strip). Offenders sao majoritariamente cores
    /// semaforicas de status (verde=ok / vermelho=erro / ambar=alerta), inclusive em bindings
    /// :class="..." do Alpine.
    /// </summary>
    private static readonly HashSet<string> DebtAllowlist = new(StringComparer.OrdinalIgnoreCase)
    {
        "Configuracoes/ConfiguracaoFiscal/Index.cshtml",
        "Diagnostico/Email.cshtml",
        "Diagnostico/Index.cshtml",
        "Diagnostico/WhatsApp.cshtml",
        "Faturas/Detail.cshtml",
        "Shared/_ModalJustificativa.cshtml",
        "Tenants/Detail.cshtml",
        "Tickets/Detail.cshtml",
        "_Layout.cshtml",
    };

    [Fact]
    public void Pages_NaoDevemUsarUtilitarioDeCorSemanticaForaDaAllowlist()
    {
        var pagesDir = ArchTestPaths.AppDirectory("EasyStock.Admin", "Pages");
        var allCshtml = Directory.GetFiles(pagesDir.FullName, "*.cshtml", SearchOption.AllDirectories);

        var offenders = new List<string>();
        foreach (var path in allCshtml)
        {
            var relative = ArchTestPaths.ToRelative(pagesDir, path);
            if (DebtAllowlist.Contains(relative)) continue;
            if (FileContainsSemanticColorUtility(path))
                offenders.Add(relative);
        }

        offenders.Should().BeEmpty(
            "paginas novas ou recem-modificadas do Admin nao podem introduzir utilitarios de cor " +
            "semantica crus do Tailwind (bg-red-*, text-emerald-*, etc.) — sao off-brand e quebram o " +
            "Deck dark-first. Use tokens via bg-[var(--crit-100)] ou as classes de marca " +
            "(navy/orange/ok/warn/crit/ink). Se for debito transitorio, adicione a DebtAllowlist (issue #705).");
    }

    [Fact]
    public void DebtAllowlist_NaoDeveConterArquivosJaLimpos()
    {
        var pagesDir = ArchTestPaths.AppDirectory("EasyStock.Admin", "Pages");
        var stale = new List<string>();
        foreach (var entry in DebtAllowlist)
        {
            var fullPath = Path.Combine(pagesDir.FullName, entry.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(fullPath))
                stale.Add($"{entry} (arquivo nao existe)");
            else if (!FileContainsSemanticColorUtility(fullPath))
                stale.Add($"{entry} (sem cor semantica crua - migrado; remova da allowlist)");
        }

        stale.Should().BeEmpty(
            "DebtAllowlist deve refletir o estado real; remova paginas ja migradas a tokens " +
            "ou inexistentes (o debito so diminui).");
    }

    private static bool FileContainsSemanticColorUtility(string path)
    {
        var content = File.ReadAllText(path);
        content = CommentRegex.Replace(content, " "); // cor em comentario nao renderiza
        return SemanticColorUtilityRegex.IsMatch(content);
    }
}
