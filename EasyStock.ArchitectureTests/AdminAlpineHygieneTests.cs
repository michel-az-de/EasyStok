using System.Text.RegularExpressions;
using FluentAssertions;

namespace EasyStock.ArchitectureTests;

/// <summary>
/// Guards anti-regressao das armadilhas Alpine.js do EasyStock.Admin — espelho do
/// <see cref="AlpineHygieneTests"/> (Web), que so lia arquivos do Web (issue #705). Cada trap
/// abaixo JA derrubou o Admin em producao e nao tinha guard estatico. Testes estaticos
/// (Category=Architecture, no gate do Husky/CI), dentro do ADR-0025 (meta-lint, sem browser/E2E).
///
/// Todos operam sobre conteudo SEM COMENTARIO (@* *@ / &lt;!-- --&gt;): tanto o _Layout quanto o
/// _CommandPalette tem comentarios que CITAM o padrao errado como exemplo cautelar (ex.: o
/// proprio "admin-components.js" e o ":key=grupo.titulo" sem fallback aparecem em comentario), o
/// que falsearia o IndexOf / o match. Mesma disciplina do RazorViewColorUtilityHygieneTests.
///
/// NOTA: o trap "init(param)" (metodo init de x-data recebendo argumento) NAO existe no Admin
/// (varredura de Pages + wwwroot/js retornou vazio); por isso nao ha guard para ele aqui.
///
/// 1. Ordem de script (#469): o core Alpine (vendor/alpine/alpine.js) deve carregar DEPOIS das
///    factories window.es* (admin-components.js); senao o &lt;es-tabs&gt; avalia esTabs antes da
///    definicao -> "esTabs is not defined", /Configuracoes inerte.
/// 2. Double-init (BUG-003/#463): &lt;body x-data="adminApp()"&gt; NAO pode ter x-init="init()"; o
///    Alpine v3 ja auto-invoca init() -> polling/fetch dobrado em TODA rota (esta no layout).
/// 3. x-for :key nulo (BUG-011/#463): o :key do x-for de grupos no command palette precisa de
///    fallback (grupo.titulo || '...'); :key null/objeto dispara "x-for key cannot be an object".
/// 4. SRI/CDN (incidente 2026-06-02): os &lt;script&gt; de Alpine devem ser self-hosted, sem
///    integrity= nem cdn.jsdelivr.net; SRI quebrado + CDN bloqueado mata o Alpine SEM logar.
/// </summary>
[Trait("Category", "Architecture")]
public class AdminAlpineHygieneTests
{
    private static readonly Regex CommentRegex = new(
        @"@\*.*?\*@|<!--.*?-->",
        RegexOptions.Compiled | RegexOptions.Singleline);

    [Fact]
    public void CoreAlpine_DeveCarregarDepoisDasFactories()
    {
        var layout = LayoutSemComentarios();
        var idxFactories = layout.IndexOf("admin-components.js", StringComparison.Ordinal);
        var idxCore = layout.IndexOf("vendor/alpine/alpine.js", StringComparison.Ordinal);

        idxFactories.Should().BeGreaterThan(-1, "o _Layout deve carregar admin-components.js (factories window.es*).");
        idxCore.Should().BeGreaterThan(-1, "o _Layout deve carregar o core vendor/alpine/alpine.js.");
        idxCore.Should().BeGreaterThan(idxFactories,
            "o core Alpine (vendor/alpine/alpine.js) deve vir DEPOIS de admin-components.js; senao o " +
            "<es-tabs> avalia esTabs antes da definicao -> 'esTabs is not defined' (#469).");
    }

    [Fact]
    public void Body_NaoDeveTerXInitRedundante()
    {
        var layout = LayoutSemComentarios();
        var bodyTag = Regex.Match(layout, @"<body\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Singleline);

        bodyTag.Success.Should().BeTrue("o _Layout deve ter a tag <body>.");
        bodyTag.Value.Should().NotContain("x-init",
            "a <body x-data=\"adminApp()\"> NAO deve ter x-init=\"init()\": o Alpine v3 auto-invoca init() " +
            "quando o objeto x-data o define; o x-init duplica -> polling/fetch dobrado em toda rota (BUG-003/#463).");
    }

    [Fact]
    public void CommandPalette_XForKey_DeveTerFallback()
    {
        var cmdk = CommentRegex.Replace(
            ArchTestPaths.ReadAppFile("EasyStock.Admin", "Pages", "Shared", "Components", "_CommandPalette.cshtml"), " ");
        cmdk.Should().MatchRegex(@":key=""grupo\.titulo\s*\|\|",
            "o :key do x-for de grupos deve ter fallback (grupo.titulo || '...'); sem ele, titulo:null " +
            "vira key objeto -> 'x-for key cannot be an object' (BUG-011/#463).");
    }

    [Fact]
    public void Alpine_DeveSerSelfHosted_SemSriNemCdn()
    {
        var layout = LayoutSemComentarios();

        layout.Should().NotContainEquivalentOf("jsdelivr",
            "o Alpine do Admin deve ser self-hosted (~/js/vendor/alpine/), nao via cdn.jsdelivr.net " +
            "(bloqueado pela rede corporativa -> Alpine morto, incidente 2026-06-02).");
        Regex.IsMatch(layout, @"<script\b[^>]*alpine[^>]*\bintegrity\s*=", RegexOptions.IgnoreCase)
            .Should().BeFalse(
            "os <script> de Alpine NAO devem ter integrity= (SRI): hash errado bloqueia o load e NAO " +
            "loga no console -> interatividade morta sem sinal (incidente 2026-06-02).");
        Regex.IsMatch(layout, @"<script\b[^>]*\bintegrity\s*=[^>]*alpine", RegexOptions.IgnoreCase)
            .Should().BeFalse("idem (integrity declarado antes do src do alpine).");
    }

    // Handler de EVENTO Alpine (x-on:*, @evt, x-init) cujo valor comeca com `{`.
    // Deliberadamente NAO casa x-data nem bindings (:class, x-bind:style), onde `{...}` e um
    // objeto literal legitimo — a diferenca e que ali o `{` e o valor esperado, e aqui e um bloco.
    private static readonly Regex HandlerAlpineComecandoComChave = new(
        @"(?:\bx-on:[\w.:-]+|\bx-init|@@\w+(?:\.\w+)*)\s*=\s*""\s*\{",
        RegexOptions.Compiled);

    [Fact]
    public void HandlersAlpine_NaoDevemComecarComBloco()
    {
        // Issue 890: o avaliador do Alpine so embrulha o handler numa IIFE async quando ele casa
        // /^[\n\s]*if.*\(.*\)/ ou /^(let|const)\s/. Fora disso ele interpola cru:
        //     with (scope) { __self.result = <handler> }
        // Um handler que comeca com `{` vira o lado direito de uma atribuicao e e parseado como
        // OBJETO LITERAL, nao como bloco: `{ const f = ... }` estoura "Unexpected identifier 'f'".
        // Foi assim que o confirm() de "cupom que nunca expira" morreu em /Cupons — sem truncamento,
        // sem escaping errado, com a expressao chegando intacta ao browser.
        // Correcao: largar as chaves externas para o handler comecar com `const`/`let`/`if(...)`.
        var pages = ArchTestPaths.AppDirectory("EasyStock.Admin", "Pages");
        var ofensores = new List<string>();

        foreach (var f in pages.EnumerateFiles("*.cshtml", SearchOption.AllDirectories))
        {
            var src = CommentRegex.Replace(File.ReadAllText(f.FullName), " ");
            foreach (Match m in HandlerAlpineComecandoComChave.Matches(src))
                ofensores.Add($"{ArchTestPaths.ToRelative(pages, f.FullName)} " +
                              $"(linha {src.Take(m.Index).Count(c => c == '\n') + 1}): {m.Value.Trim()}");
        }

        ofensores.Should().BeEmpty(
            "handler Alpine que comeca com `{` e parseado como objeto literal, nao como bloco — " +
            "remova as chaves externas para ele comecar com const/let/if(...); issue 890.");
    }

    // Atributo Alpine (x-data, x-init, x-on:..., x-if, ...) cujo valor invoca Html.Raw.
    // [^""]* nao atravessa aspas: o match so vale enquanto estiver dentro do atributo.
    private static readonly Regex AtributoAlpineComHtmlRaw = new(
        @"\bx-[\w:.@-]+\s*=\s*""[^""]*Html\.Raw",
        RegexOptions.Compiled);

    [Fact]
    public void AtributosAlpine_NaoDevemUsarHtmlRaw()
    {
        // Issue 889: `Html.Raw` desliga o encoder do Razor. Se o valor cru contiver aspas duplas
        // — e JsonSerializer.Serialize("field") devolve `"field"`, COM aspas — o parser HTML fecha
        // o atributo na primeira aspa interna. O _EmpresaPicker morreu assim: o x-data chegava ao
        // Alpine como `empresaPicker({ mode: `, derrubando /Faturas/Emitir e /Dispositivos com
        // "Unexpected token '}'" e uma cascata de "sel is not defined".
        // Sem Html.Raw o Razor emite &quot; e o browser decodifica de volta ao ler o atributo.
        var pages = ArchTestPaths.AppDirectory("EasyStock.Admin", "Pages");
        var ofensores = new List<string>();

        foreach (var f in pages.EnumerateFiles("*.cshtml", SearchOption.AllDirectories))
        {
            var src = CommentRegex.Replace(File.ReadAllText(f.FullName), " ");
            foreach (Match m in AtributoAlpineComHtmlRaw.Matches(src))
                ofensores.Add($"{ArchTestPaths.ToRelative(pages, f.FullName)} " +
                              $"(linha {src.Take(m.Index).Count(c => c == '\n') + 1}): {m.Value.Trim()}");
        }

        ofensores.Should().BeEmpty(
            "Html.Raw dentro de atributo Alpine emite aspas duplas cruas e trunca o atributo no " +
            "parser HTML — deixe o Razor encodar (@J(x), nao @Html.Raw(J(x))); issue 889.");
    }

    private static string LayoutSemComentarios() =>
        CommentRegex.Replace(ArchTestPaths.ReadAppFile("EasyStock.Admin", "Pages", "_Layout.cshtml"), " ");
}
