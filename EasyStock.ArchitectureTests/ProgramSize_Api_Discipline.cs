using FluentAssertions;

namespace EasyStock.ArchitectureTests;

/// <summary>
/// Trava regressão F3 (god Program.cs da Api).
///
/// Hoje <c>EasyStock.Api/Program.cs</c> tem 1037 linhas físicas — concentra DI,
/// observabilidade, pipeline, swagger, mobile. F3 quebra em extensions e move
/// pipeline pra <c>UseEasyStockPipeline(env)</c>, deixando o Program.cs &le; 200
/// linhas.
///
/// Enquanto F3 não fecha, este teste fica em <c>ArchitectureDebt</c> (não bloqueia
/// Husky). Quando &le; 200, troca pra <c>Architecture</c> e vira gate permanente.
///
/// Limite consistente com gate manual:
/// <c>(Get-Content EasyStock.Api/Program.cs | Measure-Object -Line).Lines</c>
/// = <c>File.ReadAllLines(path).Length</c> (linhas físicas, inclui brancos e comentários).
/// </summary>
public class ProgramSize_Api_Discipline
{
    private const string TargetRelativePath = @"EasyStock.Api\Program.cs";
    private const int MaxLines = 200;

    [Fact]
    [Trait("Category", "ArchitectureDebt")]
    public void Api_ProgramCs_DeveTerAteCemLinhas()
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
            $"EasyStock.Api/Program.cs concentra responsabilidades demais hoje ({lineCount} linhas). " +
            $"F3 quebra em Add*() extensions + UseEasyStockPipeline(env), alvo <= {MaxLines}.");
    }
}
