using System.Text.RegularExpressions;
using FluentAssertions;

namespace EasyStock.ArchitectureTests;

/// <summary>
/// Guarda contra regressao de coesao de marca em EasyStock.Web/Views/.
///
/// Bloqueia utilitarios de COR SEMANTICA crus do Tailwind (bg/text/border/ring/divide/
/// from/to/via/fill/stroke - red/amber/yellow/lime/green/emerald/teal/cyan/sky/blue/
/// violet/purple/fuchsia/pink/rose) em .cshtml. Essas cores NAO batem com os tokens de
/// marca (crit/ok/warn/navy) e — diferente dos neutros slate/gray — NAO tem remap no
/// bloco dark de tokens.css (606-648), entao quebram visualmente quando o tema escuro
/// esta ativo (toggle vivo em _Topbar.cshtml). Cores on-brand devem usar tokens via
/// utilitario arbitrario (bg-[var(--crit-100)]) ou as classes de marca ja mapeadas no
/// tailwind.config.js (navy/orange/ok/warn/crit/ink).
///
/// FORA DE ESCOPO (proposital): slate/gray (neutros, ja remapeados no dark), orange
/// (cor de marca) e indigo (aliasado a navy no tailwind.config.js:33).
///
/// Complementa CssHexHygieneTests (.css) e RazorViewHygieneTests (hex em .cshtml). A
/// DebtAllowlist torna o debito visivel + ratchet-able: ao migrar uma view p/ tokens,
/// remova-a da lista (o teste falha se um arquivo allowlistado nao tem mais cor semantica,
/// forcando a limpeza). Assim uma view NOVA nao pode introduzir cor off-brand sem decisao,
/// e o debito so diminui. Decisao de escopo (semantico-only) registrada na issue #603.
/// </summary>
[Trait("Category", "Architecture")]
public class RazorViewColorUtilityHygieneTests
{
    private static readonly Regex SemanticColorUtilityRegex = new(
        @"\b(bg|text|border|ring|divide|from|to|via|fill|stroke)-(red|amber|yellow|lime|green|emerald|teal|cyan|sky|blue|violet|purple|fuchsia|pink|rose)-[0-9]",
        RegexOptions.Compiled);

    // Comentarios Razor (@* *@) e HTML (<!-- -->) nao renderizam; cor semantica neles
    // e falso-positivo. Removidos antes de escanear (mesmo padrao do RazorViewHygiene).
    private static readonly Regex CommentRegex = new(
        @"@\*.*?\*@|<!--.*?-->",
        RegexOptions.Compiled | RegexOptions.Singleline);

    /// <summary>
    /// Views com cor semantica crua ainda nao migradas a tokens (debito documentado, issue #603).
    /// Caminhos relativos a EasyStock.Web/Views/, forward slash '/'. Ao migrar, remova daqui.
    /// </summary>
    private static readonly HashSet<string> DebtAllowlist = new(StringComparer.OrdinalIgnoreCase)
    {
        "Analytics/Index.cshtml",
        "Analytics/Movimentacoes.cshtml",
        "Anuncios/Index.cshtml",
        "Assinatura/Index.cshtml",
        "Auth/SelecionarLoja.cshtml",
        "Caixa/Historico.cshtml",
        "Caixa/Index.cshtml",
        "CaixaMobile/Index.cshtml",
        "Categorias/Index.cshtml",
        "Clientes/Detail.cshtml",
        "Clientes/Index.cshtml",
        "ClientesMobile/Index.cshtml",
        "ContasAPagar/Detalhe.cshtml",
        "ContasAReceber/Detalhe.cshtml",
        "Dashboard/Index.cshtml",
        "Dev/Components.cshtml",
        "Downloads/Index.cshtml",
        "Entradas/Nova.cshtml",
        "Entradas/Reposicao.cshtml",
        "Estoque/Index.cshtml",
        "Faq/Detalhe.cshtml",
        "Financeiro/FluxoCaixa.cshtml",
        "Fornecedores/Detail.cshtml",
        "Fornecedores/Index.cshtml",
        "Fornecedores/PedidosAbertos.cshtml",
        "Inteligencia/Index.cshtml",
        "InteligenciaLojas/Detalhe.cshtml",
        "InteligenciaLojas/Index.cshtml",
        "ListasCompras/Detail.cshtml",
        "ListasCompras/Index.cshtml",
        "ListasCompras/PedidosGerados.cshtml",
        "Lojas/Index.cshtml",
        "Lotes/Detail.cshtml",
        "Lotes/Index.cshtml",
        "LotesMobile/Index.cshtml",
        "MobileDevices/Index.cshtml",
        "MobileProducts/Divergencias.cshtml",
        "MobileProducts/Index.cshtml",
        "NotasFiscais/Detalhes.cshtml",
        "NotasFiscais/Emitir.cshtml",
        "NotasFiscais/Index.cshtml",
        "Onboarding/Index.cshtml",
        "OperacaoMobile/Index.cshtml",
        "Pedidos/Detail.cshtml",
        "Pedidos/Index.cshtml",
        "PedidosMobile/Index.cshtml",
        "Preferencias/Index.cshtml",
        "Produtos/Detail.cshtml",
        "Produtos/Form.cshtml",
        "Produtos/Index.cshtml",
        "Relatorios/Detail.cshtml",
        "Relatorios/Index.cshtml",
        "Saidas/Historico.cshtml",
        "Saidas/Nova.cshtml",
        "Shared/_BottomNav.cshtml",
        "Shared/_ConfirmModal.cshtml",
        "Shared/_PagamentosParcela.cshtml",
        "Shared/_Sidebar.cshtml",
        "Shared/_Topbar.cshtml",
        "Usuarios/Index.cshtml",
    };

    [Fact]
    public void Views_NaoDevemUsarUtilitarioDeCorSemanticaForaDaAllowlist()
    {
        var viewsDir = ArchTestPaths.AppDirectory("EasyStock.Web", "Views");
        var allCshtml = Directory.GetFiles(viewsDir.FullName, "*.cshtml", SearchOption.AllDirectories);

        var offenders = new List<string>();
        foreach (var path in allCshtml)
        {
            var relative = ArchTestPaths.ToRelative(viewsDir, path);
            if (DebtAllowlist.Contains(relative)) continue;
            if (FileContainsSemanticColorUtility(path))
                offenders.Add(relative);
        }

        offenders.Should().BeEmpty(
            "views novas ou recem-modificadas nao podem introduzir utilitarios de cor semantica " +
            "crus do Tailwind (bg-red-*, text-emerald-*, etc.) — eles sao off-brand e quebram no dark. " +
            "Use tokens via bg-[var(--crit-100)] ou as classes de marca (navy/orange/ok/warn/crit/ink). " +
            "Se for debito transitorio, adicione o arquivo a DebtAllowlist (issue #603).");
    }

    [Fact]
    public void DebtAllowlist_NaoDeveConterArquivosJaLimpos()
    {
        var viewsDir = ArchTestPaths.AppDirectory("EasyStock.Web", "Views");
        var stale = new List<string>();
        foreach (var entry in DebtAllowlist)
        {
            var fullPath = Path.Combine(viewsDir.FullName, entry.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(fullPath))
                stale.Add($"{entry} (arquivo nao existe)");
            else if (!FileContainsSemanticColorUtility(fullPath))
                stale.Add($"{entry} (sem cor semantica crua - migrado; remova da allowlist)");
        }

        stale.Should().BeEmpty(
            "DebtAllowlist deve refletir o estado real; remova views ja migradas a tokens " +
            "ou inexistentes (o debito so diminui).");
    }

    private static bool FileContainsSemanticColorUtility(string path)
    {
        var content = File.ReadAllText(path);
        content = CommentRegex.Replace(content, " "); // cor em comentario nao renderiza
        return SemanticColorUtilityRegex.IsMatch(content);
    }
}
