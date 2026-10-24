using System.Text.Json;
using FluentAssertions;

namespace EasyStock.ArchitectureTests;

/// <summary>
/// Guard (#2016, no lugar do #1588): os jobs de estoque (alertas no sino e recálculo de velocidade) voltam ligados em
/// produção porque agora rodam POR EMPRESA, cada uma num escopo de DI com o tenant ligado (<c>RodadaPorEmpresa</c>).
/// Antes, fora de request o tenant era <c>Guid.Empty</c> e o filtro global do EF zerava toda entidade com
/// <c>EmpresaId</c>: os jobs acordavam, consultavam e não faziam nada (<c>AlertasDeEstoquePorEmpresaIntegrationTests</c>
/// prova o contrário).
///
/// <para>
/// Duas travas: (1) as chaves existem e estão ligadas em <c>appsettings.Production.json</c> (<c>EnableRecalcularVelocidadesJob</c>
/// tem padrão <c>false</c> no código: sem a chave o job some em silêncio); (2) o job não volta a abrir a porta
/// de trás — passa por <c>RodadaPorEmpresa</c> e não liga o bypass de RLS, que esconderia um erro de tenant em vazamento
/// entre clientes (ADR-0010).
/// </para>
/// </summary>
[Trait("Category", "Architecture")]
public class JobsDeEstoqueLigadosPorEmpresaTests
{
    private const string ConfigDeProducao = "EasyStock.Api/appsettings.Production.json";

    [Theory]
    [InlineData("EnableAnalisadorEstoque")]
    [InlineData("EnableRecalcularVelocidadesJob")]
    public void JobDeEstoqueFicaLigadoEmProducao(string flag)
    {
        var caminho = Path.Combine(RepoPaths.FindRepoRoot(), ConfigDeProducao.Replace('/', Path.DirectorySeparatorChar));
        using var json = JsonDocument.Parse(File.ReadAllText(caminho), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });

        var jobs = json.RootElement.GetProperty("BackgroundJobs");
        jobs.TryGetProperty(flag, out var valor).Should().BeTrue(
            "{0} precisa estar escrita em {1}: sem a chave vale o padrao do codigo", flag, ConfigDeProducao);
        valor.ValueKind.Should().Be(JsonValueKind.True,
            "{0} roda por empresa (#2016) e fica ligada em producao; desligar e o rollback, feito por quem decide", flag);
        jobs.TryGetProperty("EnableAlertasEstoqueJob", out _).Should().BeFalse(
            "o job legado AlertasEstoqueJob duplicava o analisador e saiu (#2016)");
    }

    [Theory]
    [InlineData("EasyStock.Api/BackgroundServices/AnalisadorEstoqueBackgroundService.cs")]
    [InlineData("EasyStock.Api/BackgroundServices/RecalcularVelocidadesJob.cs")]
    public void JobDeEstoqueRodaPorEmpresaSemBypassDeRls(string arquivo)
    {
        var codigo = File.ReadAllText(Path.Combine(RepoPaths.FindRepoRoot(), arquivo.Replace('/', Path.DirectorySeparatorChar)));

        codigo.Should().Contain("RodadaPorEmpresa.ExecutarAsync",
            "o job percorre as empresas uma a uma, cada uma com o tenant ligado (#2016)");
        codigo.Should().NotContain("UseRowLevelSecurityBypass",
            "bypass de RLS num job de estoque esconderia um erro de tenant em vazamento entre clientes (ADR-0010)");
    }
}
