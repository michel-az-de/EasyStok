using System.Text.RegularExpressions;
using FluentAssertions;

namespace EasyStock.ArchitectureTests;

/// <summary>
/// Guards anti-regressao das 3 armadilhas Alpine.js do EasyStock.Web (Classe E, ADR-0029).
/// Cada uma ja foi corrigida E ja regrediu na pratica; estes testes estaticos
/// (Category=Architecture, no gate R4 do Husky/CI) falham se o padrao errado voltar.
/// Dentro do arcabouço do ADR-0025 (meta-lint estatico, sem browser/E2E obrigatorio).
///
/// 1. x-cloak (#103/#479, 531fcaa9): o anti-flash de dropdown deve ser [x-cloak]{display:none},
///    NUNCA uma regra persistente .app-dropdown{display:none} (x-show faz removeProperty e o CSS
///    persistente volta a esconder -> mata os menus do topbar).
/// 2. FormTagHelper (#481): o &lt;form&gt; descarta @submit.prevent no render; form-modal.js
///    precisa do listener de submit programatico, senao os modais submetem nativo e nao persistem.
/// 3. x-model (#497, 941d7821): x-model so atualiza em input/change; valor preenchido por
///    autofill/automacao nao dispara -> form-modal.js precisa disparar Event('input'/'change').
/// 4. Html.Raw em atributo Alpine (#889/#900, incidente 2026-06-18): raw desliga o encoder do
///    Razor e a primeira aspa dupla do JSON fecha o atributo -> o Alpine recebe expressao truncada.
/// </summary>
[Trait("Category", "Architecture")]
public class AlpineHygieneTests
{
    // Regra CSS persistente que case .app-dropdown (em qualquer selector) com display:none -> o trap.
    private static readonly Regex AppDropdownDisplayNone = new(
        @"\.app-dropdown[^{}]*\{[^}]*display\s*:\s*none",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    // Comentarios Razor (@* *@) e HTML (<!-- -->) nao renderizam; o padrao errado citado neles
    // e falso-positivo. Removidos antes de escanear (mesmo padrao do RazorViewHygiene).
    private static readonly Regex CommentRegex = new(
        @"@\*.*?\*@|<!--.*?-->",
        RegexOptions.Compiled | RegexOptions.Singleline);

    // Atributo Alpine (x-data, x-init, x-on:..., x-if, ...) delimitado por aspas DUPLAS cujo valor
    // invoca Html.Raw. [^""]* nao atravessa aspas: o match so vale dentro do proprio atributo.
    // Atributo em aspas simples fica de fora de proposito: aspa dupla crua nao fecha um atributo
    // delimitado por aspa simples (ex.: Produtos/Detail.cshtml, que e seguro).
    private static readonly Regex AtributoAlpineComHtmlRaw = new(
        @"\bx-[\w:.@-]+\s*=\s*""[^""]*Html\.Raw",
        RegexOptions.Compiled);

    [Fact]
    public void Dropdown_NaoDeveTerRegraDisplayNonePersistente()
    {
        var css = ArchTestPaths.ReadAppFile("EasyStock.Web","wwwroot", "css", "app.css");
        AppDropdownDisplayNone.IsMatch(css).Should().BeFalse(
            "o anti-flash de dropdown Alpine deve ser [x-cloak]{display:none}, nao uma regra " +
            "persistente .app-dropdown{display:none} -- isso mata os menus do topbar (regrediu em #103/#479).");
    }

    [Fact]
    public void FormModal_DeveManterListenerDeSubmitProgramatico()
    {
        var js = ArchTestPaths.ReadAppFile("EasyStock.Web","wwwroot", "js", "form-modal.js");
        js.Should().MatchRegex(@"addEventListener\(\s*['""]submit['""]",
            "form-modal.js deve interceptar o submit programaticamente (FormTagHelper descarta " +
            "@submit.prevent); sem isso os modais submetem nativo e nao persistem (#481).");
    }

    [Fact]
    public void Views_NaoDevemUsarDiretivaAlpineDeSubmitEmForm()
    {
        // Issue 714: o FormTagHelper descarta o atributo `@submit*` (escrito @@submit no Razor)
        // e TODOS os atributos seguintes da tag <form>. O padrao correto e x-on:submit
        // (que o TagHelper preserva) ou listener programatico no init (form-modal.js).
        // 12 views regrediram nessa classe (varredura 2026-07-02); este guard e estrito:
        // qualquer @@submit em .cshtml do Web falha — inclusive em comentario, para o
        // proximo dev nao copiar o padrao errado de um exemplo.
        var views = ArchTestPaths.AppDirectory("EasyStock.Web", "Views");
        var ofensores = views.EnumerateFiles("*.cshtml", SearchOption.AllDirectories)
            .Where(f => File.ReadAllText(f.FullName).Contains("@@submit"))
            .Select(f => ArchTestPaths.ToRelative(views, f.FullName))
            .ToList();

        ofensores.Should().BeEmpty(
            "diretivas Alpine de submit em <form> sao descartadas pelo FormTagHelper junto com os " +
            "atributos seguintes (issue 714) — use x-on:submit/x-on:submit.prevent ou listener no init().");
    }

    [Fact]
    public void Views_NaoDevemChamarInitViaXInit()
    {
        // Issue 801: o Alpine 3 auto-invoca o metodo init() definido em x-data; um
        // x-init="init()" na mesma tag roda o init DUAS vezes (listener duplicado matou o
        // cheatsheet, instanciou scanner/camera 2x, fetch duplicado no Detail). E se o
        // componente nao definir init(), a expressao quebra em runtime. Ou seja: o padrao
        // e sempre errado — renomeie para setup($el) se precisar de init explicito.
        var views = ArchTestPaths.AppDirectory("EasyStock.Web", "Views");
        var ofensores = views.EnumerateFiles("*.cshtml", SearchOption.AllDirectories)
            .Where(f => File.ReadAllText(f.FullName).Contains("x-init=\"init()\""))
            .Select(f => ArchTestPaths.ToRelative(views, f.FullName))
            .ToList();

        ofensores.Should().BeEmpty(
            "o Alpine auto-invoca init() de x-data; x-init=\"init()\" roda o init 2x (issue 801) — " +
            "remova o x-init ou renomeie o metodo para setup($el).");
    }

    [Fact]
    public void AtributosAlpine_NaoDevemUsarHtmlRaw()
    {
        // Issue 900 (mesma classe do 889/#895, e do incidente 2026-06-18 no Cardapio/Form).
        // `Html.Raw` desliga o encoder do Razor. Se o valor cru contiver aspas duplas — e
        // JsonSerializer.Serialize("Padaria") devolve `"Padaria"`, COM aspas — o parser HTML fecha
        // o atributo na primeira aspa interna. O Cardapio/Index morreu assim: o x-data chegava ao
        // Alpine como `cardapioVitrine({ titulo: `, quebrando o card "Crie sua vitrine online" em
        // TODA loja nova (o bloco so renderiza quando !Model.TemVitrine).
        // Sem Html.Raw o Razor emite &quot; e o browser decodifica de volta ao ler o atributo.
        // Par Web do guard homonimo de AdminAlpineHygieneTests (que varre EasyStock.Admin/Pages).
        var views = ArchTestPaths.AppDirectory("EasyStock.Web", "Views");
        var ofensores = new List<string>();

        foreach (var f in views.EnumerateFiles("*.cshtml", SearchOption.AllDirectories))
        {
            var src = CommentRegex.Replace(File.ReadAllText(f.FullName), " ");
            foreach (Match m in AtributoAlpineComHtmlRaw.Matches(src))
                ofensores.Add($"{ArchTestPaths.ToRelative(views, f.FullName)} " +
                              $"(linha {src.Take(m.Index).Count(c => c == '\n') + 1}): {m.Value.Trim()}");
        }

        ofensores.Should().BeEmpty(
            "Html.Raw dentro de atributo Alpine emite aspas duplas cruas e trunca o atributo no " +
            "parser HTML — deixe o Razor encodar (@(J(x)), nao @Html.Raw(J(x))); issue 900.");
    }

    [Fact]
    public void FormModal_DeveDispararInputEChangeAposPreenchimentoProgramatico()
    {
        var js = ArchTestPaths.ReadAppFile("EasyStock.Web","wwwroot", "js", "form-modal.js");
        js.Should().MatchRegex(@"new Event\(\s*['""]input['""]",
            "form-modal.js deve disparar Event('input') p/ sincronizar x-model apos preenchimento programatico/autofill (#497).");
        js.Should().MatchRegex(@"new Event\(\s*['""]change['""]",
            "form-modal.js deve disparar Event('change') idem (#497).");
    }

    [Fact]
    public void Views_ModalFixedDentroDeCardWs_DeveUsarTeleport()
    {
        // Issue 883: `.ws` aplica `backdrop-filter: blur(18px)` (app.css). Pela spec CSS,
        // backdrop-filter cria um containing block para descendentes `position: fixed`.
        // Um overlay `fixed inset-0` dentro do card deixa de cobrir o viewport (medido:
        // 1495x63 em vez de 1528x732) e e clipado pelo `overflow` do `.ws` — o botao de
        // confirmar fica inalcancavel, sem request e sem erro no console. Foi assim que o
        // botao "Estornar" do historico de saidas morreu silenciosamente.
        // Padrao correto: <template x-teleport="body"> (ja usado em Produtos/Index, #856).
        var views = ArchTestPaths.AppDirectory("EasyStock.Web", "Views");
        var ofensores = new List<string>();

        foreach (var f in views.EnumerateFiles("*.cshtml", SearchOption.AllDirectories))
        {
            var src = File.ReadAllText(f.FullName);
            if (!src.Contains("class=\"ws\"") || !src.Contains("fixed inset-0")) continue;

            foreach (Match card in CardWsAbertura.Matches(src))
            {
                var bloco = BlocoDoDivEm(src, card.Index);
                var modais = Regex.Matches(bloco, "fixed inset-0").Count;
                var teleports = Regex.Matches(bloco, "x-teleport").Count;
                if (modais > teleports)
                    ofensores.Add($"{ArchTestPaths.ToRelative(views, f.FullName)} " +
                                  $"(card na linha {src.Take(card.Index).Count(c => c == '\n') + 1}: " +
                                  $"{modais} modal(is) fixed, {teleports} teleport(s))");
            }
        }

        ofensores.Should().BeEmpty(
            "um overlay `fixed inset-0` dentro de `.ws` fica preso ao card (backdrop-filter cria " +
            "containing block) e e clipado pelo overflow — envolva o modal em " +
            "<template x-teleport=\"body\"> (issue 883).");
    }

    private static readonly Regex CardWsAbertura = new(
        @"<div class=""ws""", RegexOptions.Compiled);

    private static readonly Regex DivAberturaOuFechamento = new(
        @"<div\b|</div>", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Recorta o bloco do &lt;div&gt; que comeca em <paramref name="inicio"/>, balanceando aberturas/fechamentos.</summary>
    private static string BlocoDoDivEm(string src, int inicio)
    {
        var profundidade = 0;
        foreach (Match t in DivAberturaOuFechamento.Matches(src, inicio))
        {
            profundidade += t.Value.StartsWith("</", StringComparison.Ordinal) ? -1 : 1;
            if (profundidade == 0)
                return src[inicio..(t.Index + t.Length)];
        }
        return src[inicio..];
    }
}
