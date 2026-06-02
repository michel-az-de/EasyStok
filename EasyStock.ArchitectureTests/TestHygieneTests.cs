using System.Text.RegularExpressions;
using FluentAssertions;

namespace EasyStock.ArchitectureTests;

/// <summary>
/// Meta-teste de higiene da suite de testes (ADR-0023). Impede a regressao do
/// verde-falso de "no-op skip": uma guarda de disponibilidade de infra que sai do
/// teste com <c>return</c> em vez de asserir. Quando o Docker/DB esta fora, esse
/// teste passa VERDE sem verificar nada.
///
/// <para>
/// O padrao correto (ja usado em EasyStock.Infra.Postgre.IntegrationTests) e
/// <c>[SkippableFact]</c> + <c>Skip.If(!disponivel, motivo)</c>, que reporta o teste
/// como SKIPPED no runner — nunca PASSED.
/// </para>
///
/// <para>
/// Source-text based (mesmo idioma de <see cref="RazorViewHygieneTests"/> e
/// <see cref="ProjectFileHygieneTests"/>). Carrega <c>[Trait("Category","Architecture")]</c>
/// para rodar no gate de pre-commit (Husky) + CI. O proprio arquivo e excluido da
/// varredura (contem o padrao em regex/doc).
/// </para>
///
/// <para>
/// Ondas seguintes (#394 / ADR-0023): detector de shadow-mock (classe de teste com
/// nome de tipo de producao) e detector de <c>[Fact]</c>/<c>[Theory]</c> sem assercao.
/// </para>
/// </summary>
[Trait("Category", "Architecture")]
public class TestHygieneTests
{
    private static readonly string[] TestProjectSuffixes =
        [".Tests", ".UnitTests", ".IntegrationTests", ".ArchitectureTests"];

    // Casa "if (!<algo>Available) return;" (single-condition). Compound (ex.:
    // "if (!IsAvailable || _x is null) return;") nao casa — guardas legitimas de
    // fixture com mais de uma condicao ficam de fora.
    private static readonly Regex NoOpAvailabilitySkip = new(
        @"if\s*\(\s*!\s*[\w.]*[Aa]vailable\s*\)\s*(?:\r?\n\s*)?return\s*;",
        RegexOptions.Compiled);

    [Fact]
    public void Testes_NaoDevemUsarNoOpSkip_DevemUsarSkipIf()
    {
        var root = SolutionRoot();
        var offenders = EnumerateTestSourceFiles(root)
            .Where(f => NoOpAvailabilitySkip.IsMatch(File.ReadAllText(f)))
            .Select(f => Path.GetRelativePath(root, f).Replace('\\', '/'))
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();

        offenders.Should().BeEmpty(
            "testes nao podem pular silenciosamente via 'if (!...Available) return;' — isso passa VERDE " +
            "sem asserir nada quando a infra (Docker/DB) esta fora (verde-falso). Use [SkippableFact] + " +
            "Skip.If(!disponivel, motivo), que reporta SKIPPED em vez de PASSED. Ver ADR-0023 / #394.");
    }

    private static IEnumerable<string> EnumerateTestSourceFiles(string root) =>
        Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(f => IsInTestProject(f)
                     && !PathHasSegment(f, "obj")
                     && !PathHasSegment(f, "bin")
                     && !PathHasSegment(f, ".claude")
                     // exclui este proprio arquivo (contem o padrao em regex/doc)
                     && !Path.GetFileName(f).Equals("TestHygieneTests.cs", StringComparison.Ordinal));

    private static bool IsInTestProject(string path) =>
        path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(s => TestProjectSuffixes.Any(suf => s.EndsWith(suf, StringComparison.Ordinal)));

    private static bool PathHasSegment(string path, string segment) =>
        path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(s => s.Equals(segment, StringComparison.OrdinalIgnoreCase));

    private static string SolutionRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !current.GetFiles("*.sln").Any())
            current = current.Parent;
        return current?.FullName ?? throw new InvalidOperationException(
            "Nao foi possivel localizar a raiz da solucao.");
    }
}
