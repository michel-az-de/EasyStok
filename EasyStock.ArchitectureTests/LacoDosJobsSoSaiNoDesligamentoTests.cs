using System.Text.RegularExpressions;
using FluentAssertions;

namespace EasyStock.ArchitectureTests;

/// <summary>
/// Guard (#2023): o laço de um job só sai quando o host desliga. O <c>HttpClient</c> lança
/// <c>TaskCanceledException</c> (um <c>OperationCanceledException</c>) no timeout; um
/// <c>catch (OperationCanceledException) { break; }</c> em volta da rodada parava o job no primeiro timeout do
/// Mercado Pago e ele só voltava no próximo deploy. Sem <c>when (stoppingToken.IsCancellationRequested)</c>, sair
/// do laço por cancelamento só vale em volta de um <c>try</c> que contém apenas o <c>Task.Delay</c>, que só cancela
/// no desligamento.
/// </summary>
[Trait("Category", "Architecture")]
public class LacoDosJobsSoSaiNoDesligamentoTests
{
    private static readonly string[] Projetos =
        ["EasyStock.Api", "EasyStock.Worker", "EasyStock.Infra.Async", "EasyStock.Infra.Notifications", "EasyStock.Infra.Postgre"];

    private static readonly Regex HostedService = new(@":\s*BackgroundService\b|\bIHostedService\b", RegexOptions.Compiled);

    private static readonly Regex SaidaSemFiltro = new(
        @"catch\s*\(\s*(OperationCanceled|TaskCanceled)Exception(\s+\w+)?\s*\)\s*\{\s*(break|return);\s*\}", RegexOptions.Compiled);

    [Fact]
    public void SaidaDoLacoPorCancelamentoConfereOTokenDoHost()
    {
        var raiz = RepoPaths.FindRepoRoot();
        var violacoes = new List<string>();
        foreach (var projeto in Projetos)
        {
            foreach (var arquivo in Directory.GetFiles(Path.Combine(raiz, projeto), "*.cs", SearchOption.AllDirectories))
            {
                if (arquivo.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                    || arquivo.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")) continue;
                var linhas = File.ReadAllLines(arquivo);
                if (!linhas.Any(l => HostedService.IsMatch(l))) continue;
                for (var i = 0; i < linhas.Length; i++)
                {
                    if (SaidaSemFiltro.IsMatch(linhas[i]) && !TryContemSoODelay(linhas, i))
                        violacoes.Add($"{Path.GetRelativePath(raiz, arquivo)}:{i + 1}");
                }
            }
        }

        violacoes.Should().BeEmpty(
            "timeout de HTTP é OperationCanceledException e não pode parar o job; use catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)");
    }

    /// <summary>O <c>try</c> fechado logo antes do <c>catch</c> da linha <paramref name="indiceDoCatch"/> só aguarda o <c>Task.Delay</c>.</summary>
    private static bool TryContemSoODelay(string[] linhas, int indiceDoCatch)
    {
        // try { await Task.Delay(...); } catch (OperationCanceledException) { break; } numa linha só.
        var antesDoCatch = linhas[indiceDoCatch][..SaidaSemFiltro.Match(linhas[indiceDoCatch]).Index];
        if (Regex.IsMatch(antesDoCatch, @"\btry\b")) return Regex.IsMatch(antesDoCatch, @"^\s*try\s*\{\s*await\s+Task\.Delay\([^;]*;\s*\}\s*$");

        var bloco = new List<string>();
        var profundidade = 0;
        for (var j = indiceDoCatch - 1; j >= 0; j--)
        {
            bloco.Insert(0, linhas[j]);
            profundidade += linhas[j].Count(c => c == '}') - linhas[j].Count(c => c == '{');
            if (profundidade <= 0 && Regex.IsMatch(linhas[j], @"\btry\b")) break;
        }

        var texto = string.Join('\n', bloco);
        return Regex.Matches(texto, @"\bawait\b").Count == 1 && Regex.IsMatch(texto, @"\bawait\s+Task\.Delay\(");
    }
}
