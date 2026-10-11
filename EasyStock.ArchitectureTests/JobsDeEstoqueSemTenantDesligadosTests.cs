using System.Text.Json;
using FluentAssertions;

namespace EasyStock.ArchitectureTests;

/// <summary>
/// Guard (#1588): o analisador de estoque e o recálculo de velocidades ficam desligados em produção enquanto
/// rodarem sem tenant. Fora de request o tenant é <c>Guid.Empty</c> e o filtro global do EF zera toda entidade
/// com <c>EmpresaId</c>: os dois jobs acordavam (a cada 60 min e a cada 6 h), consultavam e não faziam nada
/// (<c>JobsDeEstoqueSemTenantIntegrationTests</c> prova). Religar só depois de dar tenant aos jobs, com
/// consulta em lote, filtro de saldo e retenção das notificações.
///
/// <c>EnableAnalisadorEstoque</c> tem padrão <c>true</c> em <c>BackgroundJobOptions</c>: a chave precisa
/// existir com <c>false</c>, tirar a linha religa o job.
/// </summary>
[Trait("Category", "Architecture")]
public class JobsDeEstoqueSemTenantDesligadosTests
{
    private const string ConfigDeProducao = "EasyStock.Api/appsettings.Production.json";

    [Theory]
    [InlineData("EnableAnalisadorEstoque")]
    [InlineData("EnableRecalcularVelocidadesJob")]
    public void JobSemTenantFicaDesligadoEmProducao(string flag)
    {
        var caminho = Path.Combine(RepoPaths.FindRepoRoot(), ConfigDeProducao.Replace('/', Path.DirectorySeparatorChar));
        using var json = JsonDocument.Parse(File.ReadAllText(caminho), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });

        var jobs = json.RootElement.GetProperty("BackgroundJobs");
        jobs.TryGetProperty(flag, out var valor).Should().BeTrue(
            "{0} precisa estar escrita em {1}: sem a chave vale o padrao do codigo", flag, ConfigDeProducao);
        valor.ValueKind.Should().Be(JsonValueKind.False,
            "{0} roda sem tenant e nao le nada (#1588); religue so depois de dar tenant ao job", flag);
    }
}
