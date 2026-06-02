using FluentAssertions;
using System.Text.RegularExpressions;

namespace EasyStock.ArchitectureTests;

/// <summary>
/// Trava regressão F11 (catch genérico engolindo exception sem log em Admin/Pages).
///
/// Hoje ~20 Razor Pages do <c>EasyStock.Admin/Pages</c> têm
/// <c>catch (Exception ex) { Erro = ex.Message; }</c> sem log. F11 injeta
/// <c>ILogger&lt;TPage&gt;</c> e adiciona <c>_logger.LogError(ex, "...")</c> em
/// cada bloco.
///
/// Heurística textual:
/// <list type="bullet">
///   <item>Conta ocorrências de <c>catch (Exception</c> em <c>*.cshtml.cs</c>.</item>
///   <item>Conta as que TÊM <c>_logger.Log</c> (ou <c>logger.Log</c>) próximas no bloco.</item>
///   <item>Compara — diferença = catches sem log.</item>
/// </list>
///
/// Limitações reconhecidas (regex não pega):
/// <list type="bullet">
///   <item><c>catch (Exception ex) when (...)</c> (guard clause).</item>
///   <item>Log fora do bloco <c>catch</c> (escopo maior).</item>
///   <item><c>Log.Error</c> estático (Serilog static).</item>
///   <item>Try aninhado.</item>
/// </list>
/// Documentado — revisão humana complementa.
/// </summary>
public class RazorPagesCatchDiscipline
{
    // Forward slash: Path.Combine resolve em Windows E Linux (CI roda em ubuntu).
    private const string PagesRelativePath = "EasyStock.Admin/Pages";

    private static readonly Regex CatchPattern = new(
        @"catch\s*\(\s*Exception\b",
        RegexOptions.Compiled | RegexOptions.Multiline);

    private static readonly Regex CatchWithLogPattern = new(
        @"catch\s*\(\s*Exception[^)]*\)\s*\{[^}]{0,800}?(?:(?:_logger|logger|\blog)\.Log|SetErroSeguro)",
        RegexOptions.Compiled | RegexOptions.Singleline);

    [Fact]
    [Trait("Category", "Architecture")]
    public void Admin_Pages_CatchException_DeveTer_LogErrorAssociado()
    {
        // Arrange
        var repoRoot = RepoPaths.FindRepoRoot();
        var pagesPath = Path.Combine(repoRoot, PagesRelativePath);

        Directory.Exists(pagesPath).Should().BeTrue(
            $"diretório {pagesPath} sumiu — Admin/Pages foi movido? Atualizar este teste.");

        var pageFiles = Directory.EnumerateFiles(pagesPath, "*.cshtml.cs", SearchOption.AllDirectories)
            .ToArray();

        pageFiles.Should().NotBeEmpty(
            $"esperado .cshtml.cs em {pagesPath} — F11 trabalha em cima dessas pages.");

        // Act
        var offenders = new List<string>();
        foreach (var path in pageFiles)
        {
            var content = File.ReadAllText(path);
            var catches = CatchPattern.Matches(content).Count;
            if (catches == 0) continue;

            var catchesWithLog = CatchWithLogPattern.Matches(content).Count;
            var missing = catches - catchesWithLog;
            if (missing > 0)
            {
                offenders.Add($"{Path.GetRelativePath(repoRoot, path)} ({missing} sem log)");
            }
        }

        // Assert
        offenders.Should().BeEmpty(
            "Razor Pages do Admin devem logar exceptions capturadas no catch — F11. " +
            "Heurística textual: catch sem _logger.Log/logger.Log próximo. " +
            $"Pages em violação: {string.Join("; ", offenders)}");
    }
}
