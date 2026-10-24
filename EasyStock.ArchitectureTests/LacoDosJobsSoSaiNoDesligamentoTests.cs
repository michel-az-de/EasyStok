using System.Text.RegularExpressions;
using FluentAssertions;

namespace EasyStock.ArchitectureTests;

/// <summary>
/// Guard (#2023): o laço de um job só sai quando o host desliga. O <c>HttpClient</c> lança
/// <c>TaskCanceledException</c> (um <c>OperationCanceledException</c>) no timeout; um
/// <c>catch (OperationCanceledException) { break; }</c> em volta da rodada parava o job no primeiro timeout do
/// Mercado Pago e ele só voltava no próximo deploy. Sem <c>when (stoppingToken.IsCancellationRequested)</c>, o
/// <c>break</c> só vale em volta do <c>Task.Delay</c> do próprio laço, que só cancela no desligamento.
/// </summary>
[Trait("Category", "Architecture")]
public class LacoDosJobsSoSaiNoDesligamentoTests
{
    private static readonly string[] Pastas =
    [
        Path.Combine("EasyStock.Api", "BackgroundServices"),
        Path.Combine("EasyStock.Worker", "BackgroundServices"),
    ];

    private static readonly Regex SaidaSemFiltro = new(@"catch\s*\(\s*OperationCanceledException\s*\)\s*\{\s*break;\s*\}", RegexOptions.Compiled);

    [Fact]
    public void SaidaDoLacoPorCancelamentoConfereOTokenDoHost()
    {
        var raiz = RepoPaths.FindRepoRoot();
        var violacoes = new List<string>();
        foreach (var pasta in Pastas)
        {
            foreach (var arquivo in Directory.GetFiles(Path.Combine(raiz, pasta), "*.cs", SearchOption.AllDirectories))
            {
                var linhas = File.ReadAllLines(arquivo);
                for (var i = 0; i < linhas.Length; i++)
                {
                    if (!SaidaSemFiltro.IsMatch(linhas[i])) continue;
                    if (i > 0 && linhas[i - 1].Contains("Task.Delay(", StringComparison.Ordinal)) continue;
                    violacoes.Add($"{Path.GetRelativePath(raiz, arquivo)}:{i + 1}");
                }
            }
        }

        violacoes.Should().BeEmpty(
            "timeout de HTTP é OperationCanceledException e não pode parar o job; use catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)");
    }
}
