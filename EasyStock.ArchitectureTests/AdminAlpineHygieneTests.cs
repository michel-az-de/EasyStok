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

    private static string LayoutSemComentarios() =>
        CommentRegex.Replace(ArchTestPaths.ReadAppFile("EasyStock.Admin", "Pages", "_Layout.cshtml"), " ");
}
