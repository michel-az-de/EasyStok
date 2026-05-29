using FluentAssertions;

namespace EasyStock.ArchitectureTests;

/// <summary>
/// Trava regressão F4 (god Program.cs do Admin).
///
/// Hoje <c>EasyStock.Admin/Program.cs</c> tem 932 linhas físicas. F4 espelha F3:
/// quebra em extensions + UseEasyStockPipeline(env), alvo &le; 200 linhas.
///
/// Mantida separada da regra Api de propósito: se fossem uma regra só promovida
/// pra <c>Architecture</c> quando F3 fechar, bloquearia Husky durante toda a F4
/// (Admin ainda > 200). Independentes.
///
/// Limite consistente com gate manual:
/// <c>(Get-Content EasyStock.Admin/Program.cs | Measure-Object -Line).Lines</c>
/// = <c>File.ReadAllLines(path).Length</c>.
/// </summary>
public class ProgramSize_Admin_Discipline
{
    private const string TargetRelativePath = @"EasyStock.Admin\Program.cs";
    private const int MaxLines = 200;

    [Fact]
    [Trait("Category", "ArchitectureDebt")]
    public void Admin_ProgramCs_DeveTerAteCemLinhas()
    {
        // Arrange
        var repoRoot = RepoPaths.FindRepoRoot();
        var targetPath = Path.Combine(repoRoot, TargetRelativePath);

        File.Exists(targetPath).Should().BeTrue(
            $"alvo {targetPath} sumiu — Program.cs foi movido? Atualizar este teste.");

        // Act
        var lineCount = File.ReadAllLines(targetPath).Length;

        // Assert
        lineCount.Should().BeLessThanOrEqualTo(MaxLines,
            $"EasyStock.Admin/Program.cs concentra responsabilidades demais hoje ({lineCount} linhas). " +
            $"F4 quebra em Add*() extensions + UseEasyStockPipeline(env), alvo <= {MaxLines}.");
    }
}
