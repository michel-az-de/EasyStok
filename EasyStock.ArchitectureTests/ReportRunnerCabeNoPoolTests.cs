using System.Text.RegularExpressions;
using FluentAssertions;

namespace EasyStock.ArchitectureTests;

/// <summary>
/// Guard (#1575): o padrão de relatórios simultâneos do Worker cabe no pool de conexões padrão. Cada run em
/// voo segura uma conexão no stream do handler e abre outra, curta, no heartbeat de 30 s (o start usa uma
/// antes do stream, nunca junto). O padrão era 20 runs contra um pool de 10: com a fila cheia, o heartbeat
/// esperava conexão, o lease vencia e o relatório duplicava, e o dispatcher, o outbox e os lembretes do
/// mesmo processo ficavam sem conexão.
///
/// Source-text based (idioma de <see cref="EmailFabricaUnicaTests"/>): nenhum projeto de teste referencia o Worker.
/// </summary>
[Trait("Category", "Architecture")]
public class ReportRunnerCabeNoPoolTests
{
    private const string Runner = "EasyStock.Worker/BackgroundServices/ReportRunnerBackgroundService.cs";
    private const string Pool = "EasyStock.Infra.Postgre/DependencyInjection/ServiceCollectionExtensions.cs";

    private const int ConexoesPorRun = 2;

    // O resto do Worker ao mesmo tempo: dispatcher, avaliador e coletor de notificações, o LISTEN do outbox,
    // o outbox de integração e uma rodada de job com a trava (pick e watchdog dos relatórios cabem na sobra).
    private const int FolgaDosOutrosServicos = 6;

    [Fact]
    public void PadraoDeRunsSimultaneasDeixaFolgaNoPoolPadrao()
    {
        var root = RepoPaths.FindRepoRoot();
        var runs = Numero(root, Runner, @"\bDefaultMaxConcurrentInstances\s*=\s*(\d+)");
        var pool = Numero(root, Pool, @"""Postgres:Pool:Max""\s*\)\s*\?\?\s*(\d+)");

        (runs * ConexoesPorRun + FolgaDosOutrosServicos).Should().BeLessThanOrEqualTo(pool,
            "{0} runs x {1} conexoes + {2} de folga precisam caber no pool padrao de {3}; quem quiser mais sobe "
            + "Reporting:Runner:MaxConcurrentRunsPerInstance junto com Postgres:Pool:Max",
            runs, ConexoesPorRun, FolgaDosOutrosServicos, pool);
        runs.Should().BeGreaterThan(0);
    }

    private static int Numero(string root, string relativo, string padrao)
    {
        var codigo = File.ReadAllText(Path.Combine(root, relativo.Replace('/', Path.DirectorySeparatorChar)));
        var achado = Regex.Match(codigo, padrao);
        achado.Success.Should().BeTrue("o guard le o numero em {0} por /{1}/; se o codigo mudou, ajuste o guard", relativo, padrao);
        return int.Parse(achado.Groups[1].Value);
    }
}
