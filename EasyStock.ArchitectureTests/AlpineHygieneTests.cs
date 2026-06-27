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
/// </summary>
[Trait("Category", "Architecture")]
public class AlpineHygieneTests
{
    // Regra CSS persistente que case .app-dropdown (em qualquer selector) com display:none -> o trap.
    private static readonly Regex AppDropdownDisplayNone = new(
        @"\.app-dropdown[^{}]*\{[^}]*display\s*:\s*none",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

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
    public void FormModal_DeveDispararInputEChangeAposPreenchimentoProgramatico()
    {
        var js = ArchTestPaths.ReadAppFile("EasyStock.Web","wwwroot", "js", "form-modal.js");
        js.Should().MatchRegex(@"new Event\(\s*['""]input['""]",
            "form-modal.js deve disparar Event('input') p/ sincronizar x-model apos preenchimento programatico/autofill (#497).");
        js.Should().MatchRegex(@"new Event\(\s*['""]change['""]",
            "form-modal.js deve disparar Event('change') idem (#497).");
    }

}
