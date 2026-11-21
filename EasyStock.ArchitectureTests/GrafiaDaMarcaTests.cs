using System.Text.RegularExpressions;
using FluentAssertions;

namespace EasyStock.ArchitectureTests;

/// <summary>
/// #2517: a marca se escreve "Casa da Baba", sem acento (regra canônica do AGENTS.md).
/// O teste falha se a grafia com acento voltar em qualquer arquivo de texto do repositório.
/// A letra acentuada é montada por código de caractere para este arquivo não conter a grafia proibida.
/// </summary>
[Trait("Category", "Architecture")]
public class GrafiaDaMarcaTests
{
    private static readonly Regex GrafiaComAcento = new("bab[" + (char)0xE1 + (char)0xC1 + "]", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly HashSet<string> PastasIgnoradas = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", "bin", "obj", "node_modules", "dist", ".vs", ".claude", "TestResults", "playwright-report", "test-results",
    };

    private static readonly HashSet<string> Extensoes = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs", ".cshtml", ".razor", ".js", ".jsx", ".mjs", ".ts", ".tsx", ".css", ".html", ".json", ".md", ".yml", ".yaml",
        ".ps1", ".sh", ".sql", ".txt", ".xml", ".csproj", ".props", ".resx", ".svg", ".dart", ".kt", ".arb",
    };

    [Fact]
    public void Marca_NaoAparece_ComAcento()
    {
        var raiz = ArchTestPaths.SolutionRoot();
        var achados = new List<string>();

        foreach (var arquivo in Arquivos(new DirectoryInfo(raiz)))
        {
            var linhas = File.ReadAllLines(arquivo.FullName);
            for (var i = 0; i < linhas.Length; i++)
            {
                if (GrafiaComAcento.IsMatch(linhas[i]))
                    achados.Add($"{Path.GetRelativePath(raiz, arquivo.FullName)}:{i + 1}");
            }
        }

        achados.Should().BeEmpty("a marca é \"Casa da Baba\", sem acento (AGENTS.md, #2517)");
    }

    private static IEnumerable<FileInfo> Arquivos(DirectoryInfo pasta)
    {
        foreach (var arquivo in pasta.EnumerateFiles())
        {
            if (Extensoes.Contains(arquivo.Extension)) yield return arquivo;
        }

        foreach (var sub in pasta.EnumerateDirectories())
        {
            if (PastasIgnoradas.Contains(sub.Name)) continue;
            foreach (var arquivo in Arquivos(sub)) yield return arquivo;
        }
    }
}
