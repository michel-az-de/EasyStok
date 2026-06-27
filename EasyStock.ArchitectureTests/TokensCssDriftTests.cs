using System.Text.RegularExpressions;
using FluentAssertions;

namespace EasyStock.ArchitectureTests;

/// <summary>
/// Drift guard dos design tokens entre EasyStock.Web e EasyStock.Admin (Fase 0-core).
///
/// Os dois apps compartilham o MESMO conjunto de design tokens (tokens.css), mas cada
/// um serve o seu arquivo. Este teste trava a divergencia de VALOR: um token presente
/// nos dois (ex.: --navy-700) precisa ter o mesmo valor. Pega o classico "mudou
/// --orange-600 no Web e esqueceu o Admin".
///
/// Por que NAO byte-igualdade: cada arquivo tem CSS de componente proprio (o do Web tem
/// form-field/tooltip/sticky; o do Api/api-docs e um conjunto totalmente a parte de 139
/// linhas). Forcar arquivos identicos incharia o Api. Entao comparamos so a INTERSECCAO
/// de tokens. Usa a primeira ocorrencia de cada token (o :root light, canonico); os
/// overrides do tema dark sao ignorados de proposito.
/// </summary>
[Trait("Category", "Architecture")]
public class TokensCssDriftTests
{
    private static readonly Regex TokenDecl = new(
        @"--([A-Za-z0-9-]+)\s*:\s*([^;{}]+);",
        RegexOptions.Compiled);

    [Fact]
    public void TokensCompartilhados_Web_e_Admin_TemMesmoValor()
    {
        var web = ParseTokens(TokensPath("EasyStock.Web"));
        var admin = ParseTokens(TokensPath("EasyStock.Admin"));

        var drift = new List<string>();
        foreach (var name in web.Keys.Intersect(admin.Keys).OrderBy(k => k, StringComparer.Ordinal))
        {
            if (web[name] != admin[name])
                drift.Add($"--{name}: Web='{web[name]}' vs Admin='{admin[name]}'");
        }

        drift.Should().BeEmpty(
            "tokens compartilhados entre Web e Admin devem ter o mesmo valor (fonte de " +
            "design unica). Se um foi alterado, atualize os dois.\nDivergencias:\n" +
            string.Join("\n", drift));
    }

    [Fact]
    public void Web_e_Admin_CompartilhamTokensSuficientes()
    {
        // Sanity: impede que o teste acima passe por vacuidade (parser quebrado ->
        // interseccao vazia -> falso verde). Web e Admin compartilham dezenas de tokens.
        var web = ParseTokens(TokensPath("EasyStock.Web"));
        var admin = ParseTokens(TokensPath("EasyStock.Admin"));
        web.Keys.Intersect(admin.Keys).Count().Should().BeGreaterThan(30,
            "Web e Admin compartilham a rampa de cor inteira + surfaces + radii + etc.; " +
            "interseccao pequena demais indica parser quebrado.");
    }

    private static Dictionary<string, string> ParseTokens(string path)
    {
        File.Exists(path).Should().BeTrue($"tokens.css esperado em {path}");
        var content = File.ReadAllText(path);
        var dict = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match m in TokenDecl.Matches(content))
        {
            var name = m.Groups[1].Value;
            if (dict.ContainsKey(name)) continue; // primeira ocorrencia = :root light (canonico)
            var value = Regex.Replace(m.Groups[2].Value.Trim(), @"\s+", " ").ToLowerInvariant();
            dict[name] = value;
        }
        return dict;
    }

    private static string TokensPath(string project)
    {
        var root = ArchTestPaths.SolutionRoot();
        return Path.Combine(root, project, "wwwroot", "css", "tokens.css");
    }
}
