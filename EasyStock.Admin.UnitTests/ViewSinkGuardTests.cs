using System.Text.RegularExpressions;
using FluentAssertions;

namespace EasyStock.Admin.UnitTests;

/// <summary>
/// Guard estrutural (lint) que trava a CLASSE do F0, não só as 2 instâncias corrigidas:
/// nenhuma view pode reintroduzir o idioma de escape JS manual nem emitir dado de tenant
/// cru. Era VERMELHO no código pré-fix (Tenants/Detail tinha `.Replace("'", "\\'")` ×3 e
/// `@Html.Raw(unomeEscaped)`); fica VERDE com o fix data-*.
/// </summary>
public class ViewSinkGuardTests
{
    private static string PagesDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "EasyStok.sln")))
            dir = dir.Parent;
        dir.Should().NotBeNull("o teste precisa achar a raiz do repo (EasyStok.sln)");
        return Path.Combine(dir!.FullName, "EasyStock.Admin", "Pages");
    }

    private static IEnumerable<string> AllCshtml() =>
        Directory.EnumerateFiles(PagesDir(), "*.cshtml", SearchOption.AllDirectories);

    [Fact]
    public void Nenhuma_view_usa_escape_JS_manual_de_aspa_simples()
    {
        // O idioma frágil que causou o F0. Banir em TODAS as views (lock da classe, H1).
        var ofensores = AllCshtml()
            .Where(f => File.ReadAllText(f).Contains(@".Replace(""'"", ""\'"")"))
            .Select(f => Path.GetFileName(f))
            .ToList();

        ofensores.Should().BeEmpty(
            "escape JS manual (.Replace(\"'\", \"\\'\")) não cobre aspa dupla/`<` → use data-* + encoder do Razor");
    }

    [Fact]
    public void TenantsDetail_roteia_dado_de_tenant_por_data_attributes()
    {
        var src = File.ReadAllText(Path.Combine(PagesDir(), "Tenants", "Detail.cshtml"));

        // Fix presente: campos de tenant saem por data-* (Razor encoda o atributo).
        src.Should().Contain("data-unome=\"@unome\"");
        src.Should().Contain("data-uemail=\"@uemail\"");
        src.Should().Contain("data-loja=\"@lojaJson\"");
        src.Should().Contain("data-lnome=\"@lnome\"");

        // Vetor antigo ausente.
        src.Should().NotContain("unomeEscaped");
        src.Should().NotContain("uemailEscaped");
        src.Should().NotContain("lnomeEscaped");
        src.Should().NotContain("lojaJsonAttr");
    }

    [Fact]
    public void Diagnostico_nao_emite_StatusData_cru_em_script()
    {
        var src = File.ReadAllText(Path.Combine(PagesDir(), "Diagnostico", "Index.cshtml"));
        src.Should().NotContain("Html.Raw(Model.StatusData.GetRawText())",
            "GetRawText() cru permitiria breakout </script>; usar JsonSerializer.Serialize");
        src.Should().Contain("JsonSerializer.Serialize(Model.StatusData)");
    }

    [Fact]
    public void Nenhum_x_html_consome_campo_de_tenant()
    {
        // Anti-recursão (A6/A7): se um x-html passar a consumir _unome/_uemail/lnome,
        // o encode server-side não basta mais. Banir explicitamente.
        var rx = new Regex(@"x-html\s*=\s*[""'][^""']*(_unome|_uemail|_uid|unome|uemail|lnome)", RegexOptions.IgnoreCase);
        var ofensores = AllCshtml()
            .Where(f => rx.IsMatch(File.ReadAllText(f)))
            .Select(Path.GetFileName)
            .ToList();

        ofensores.Should().BeEmpty("dado de tenant não pode fluir para x-html (sink de HTML runtime)");
    }
}
