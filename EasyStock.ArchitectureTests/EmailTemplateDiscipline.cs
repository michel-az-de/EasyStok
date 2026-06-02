using FluentAssertions;

namespace EasyStock.ArchitectureTests;

/// <summary>
/// Trava regressão F6 (HTML chumbado em <c>NotificacoesGlobaisSeed.cs</c>).
///
/// Hoje 17 templates de email estão inline como string literals em
/// <c>EasyStock.Api/Data/NotificacoesGlobaisSeed.cs</c>. F6 migra cada um
/// pra arquivo <c>.html</c> físico (EmbeddedResource). Enquanto F6 não fecha,
/// este teste fica em <c>ArchitectureDebt</c> (não bloqueia Husky). Quando os
/// 17 templates migrarem, troca pra <c>Architecture</c> e vira gate permanente.
///
/// Heurística textual (regex de tag HTML), não garantia formal — documentar.
/// </summary>
public class EmailTemplateDiscipline
{
    // Forward slash: Path.Combine resolve em Windows E Linux (CI roda em ubuntu).
    private const string SeedRelativePath =
        "EasyStock.Api/Data/NotificacoesGlobaisSeed.cs";

    private static readonly string[] HtmlTagPatterns =
    {
        "<html", "<body", "<table", "<div", "<p>", "<p ",
        "<strong>", "<a href", "<span", "<br",
    };

    [Fact]
    [Trait("Category", "Architecture")]
    public void NotificacoesGlobaisSeed_NaoDeveConter_TagHtml()
    {
        // Arrange
        var repoRoot = RepoPaths.FindRepoRoot();
        var seedPath = Path.Combine(repoRoot, SeedRelativePath);

        // Falha explícita se o alvo sumiu — refactor/renomeação detectada,
        // teste precisa ser atualizado em vez de virar silenciosamente verde.
        File.Exists(seedPath).Should().BeTrue(
            $"alvo {seedPath} sumiu — F6 renomeou/moveu o seed? Atualizar este teste.");

        var content = File.ReadAllText(seedPath);

        // Act
        var hits = HtmlTagPatterns
            .Where(pattern => content.Contains(pattern, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        // Assert
        hits.Should().BeEmpty(
            "NotificacoesGlobaisSeed deve ler templates de arquivos .html embedded " +
            "(F6). Tags HTML inline indicam templates ainda chumbados. " +
            $"Patterns encontrados: {string.Join(", ", hits)}");
    }
}
