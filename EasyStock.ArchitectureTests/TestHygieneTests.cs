using System.Text.RegularExpressions;
using FluentAssertions;

namespace EasyStock.ArchitectureTests;

/// <summary>
/// Meta-testes de higiene da suite (ADR-0023). Source-text based (idioma de
/// <see cref="RazorViewHygieneTests"/>). Todos carregam
/// <c>[Trait("Category","Architecture")]</c> — rodam no gate Husky + CI.
///
/// <list type="bullet">
///   <item>(a) No-op skip: bane <c>if (!...Available) return;</c> — passa VERDE sem asserir.</item>
///   <item>(c) Shadow-mock: bane classe de teste cujo nome simples existe em producao —
///       o teste exercita a reimplementacao, nao o codigo real.</item>
/// </list>
/// </summary>
[Trait("Category", "Architecture")]
public class TestHygieneTests
{
    private static readonly string[] TestProjectSuffixes =
        [".Tests", ".UnitTests", ".IntegrationTests", ".ArchitectureTests"];

    private static readonly string[] ProductionProjectPrefixes =
        ["EasyStock.Domain", "EasyStock.Application", "EasyStock.Api",
         "EasyStock.Infra.", "EasyStock.Web", "EasyStock.Admin",
         "EasyStock.Worker", "EasyStock.Contracts"];

    // (a) Casa "if (!<algo>Available) return;" (single-condition).
    private static readonly Regex NoOpAvailabilitySkip = new(
        @"if\s*\(\s*!\s*[\w.]*[Aa]vailable\s*\)\s*(?:\r?\n\s*)?return\s*;",
        RegexOptions.Compiled);

    // (c) Extrai nomes simples de tipos declarados num arquivo .cs
    private static readonly Regex TypeDeclaration = new(
        @"(?:^|\s)(?:public|internal)\s+(?:sealed\s+|abstract\s+|partial\s+)*(?:class|record|struct|interface)\s+(\w+)",
        RegexOptions.Compiled | RegexOptions.Multiline);

    // ── (a) no-op skip ────────────────────────────────────────────────────

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
            "testes nao podem pular silenciosamente via 'if (!...Available) return;' — passa VERDE " +
            "sem asserir quando infra esta fora (verde-falso). Use [SkippableFact] + " +
            "Skip.If(!disponivel, motivo). Ver ADR-0023 / #394.");
    }

    // ── (c) shadow-mock ───────────────────────────────────────────────────

    [Fact]
    public void Testes_NaoDevemDefinirClasse_ComNomeIgualATipoDeProducao()
    {
        var root = SolutionRoot();

        // Coleta nomes simples declarados em projetos de producao
        var prodNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in EnumerateProductionSourceFiles(root))
        {
            var content = File.ReadAllText(file);
            foreach (Match m in TypeDeclaration.Matches(content))
                prodNames.Add(m.Groups[1].Value);
        }

        // Busca colisoes em projetos de teste
        var offenders = new List<string>();
        foreach (var file in EnumerateTestSourceFiles(root))
        {
            var content = File.ReadAllText(file);
            foreach (Match m in TypeDeclaration.Matches(content))
            {
                var name = m.Groups[1].Value;
                if (prodNames.Contains(name))
                    offenders.Add($"{Path.GetRelativePath(root, file).Replace('\\', '/')}: '{name}'");
            }
        }

        offenders.Should().BeEmpty(
            "classe/record de teste com nome igual a tipo de producao e shadow-mock — o teste exercita " +
            "a reimplementacao, nao o codigo real. Renomear o tipo de teste (ex: FooDto -> TestFooDto / " +
            "FooResponseDto) ou mover o tipo compartilhado para EasyStock.Contracts. Ver ADR-0023 / #394.");
    }

    // ── helpers ───────────────────────────────────────────────────────────

    private static IEnumerable<string> EnumerateTestSourceFiles(string root) =>
        Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(f => IsInTestProject(f)
                     && !PathHasSegment(f, "obj")
                     && !PathHasSegment(f, "bin")
                     && !PathHasSegment(f, ".claude")
                     && !Path.GetFileName(f).Equals("TestHygieneTests.cs", StringComparison.Ordinal));

    private static IEnumerable<string> EnumerateProductionSourceFiles(string root) =>
        Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(f => IsInProductionProject(f)
                     && !PathHasSegment(f, "obj")
                     && !PathHasSegment(f, "bin")
                     && !PathHasSegment(f, ".claude"));

    private static bool IsInTestProject(string path) =>
        path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(s => TestProjectSuffixes.Any(suf => s.EndsWith(suf, StringComparison.Ordinal)));

    private static bool IsInProductionProject(string path)
    {
        var segments = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return segments.Any(s =>
            ProductionProjectPrefixes.Any(p => s.StartsWith(p, StringComparison.Ordinal))
            && !TestProjectSuffixes.Any(suf => s.EndsWith(suf, StringComparison.Ordinal)));
    }

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
