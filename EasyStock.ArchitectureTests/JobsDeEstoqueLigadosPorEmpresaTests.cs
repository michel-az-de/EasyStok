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
/// Quatro travas: (1) as chaves existem e estão ligadas em <c>appsettings.Production.json</c> (<c>EnableRecalcularVelocidadesJob</c>
/// tem padrão <c>false</c> no código: sem a chave o job some em silêncio); (2) o job não volta a abrir a porta
/// de trás — passa por <c>RodadaPorEmpresa</c> e não liga o bypass de RLS, que esconderia um erro de tenant em vazamento
/// entre clientes (ADR-0010); (3) as chaves de deduplicação por condição e de retenção existem em <c>appsettings.json</c>;
/// (4) o gerador de alertas lê os alertas vigentes numa consulta e expurga na própria rodada, e os testes de Postgres que
/// provam a deduplicação e a retenção seguem no repositório. Ligar o job sem isso (as três condições do #1588: consulta em
/// lote, filtro de saldo e retenção) joga o mesmo aviso no sino todo dia e o sino da equipe vira ruído.
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

    [Fact]
    public void AlertasDeEstoqueTemChavesDeDeduplicacaoERetencao()
    {
        var caminho = Path.Combine(RepoPaths.FindRepoRoot(), "EasyStock.Api", "appsettings.json");
        using var json = JsonDocument.Parse(File.ReadAllText(caminho), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });

        var alertas = json.RootElement.GetProperty("BackgroundJobs").GetProperty("AlertasEstoque");
        var dias = alertas.GetProperty("DiasSemRepetir").GetInt32();
        var retencao = alertas.GetProperty("RetencaoDias").GetInt32();

        dias.Should().BeGreaterThanOrEqualTo(1, "sem janela o alerta volta todo ciclo");
        retencao.Should().BeGreaterThanOrEqualTo(dias, "a retenção não pode apagar o alerta que ainda segura a repetição");
    }

    [Fact]
    public void GeradorDeAlertasDeduplicaPorCondicaoEExpurgaNaRodada()
    {
        var raiz = RepoPaths.FindRepoRoot();
        var gerador = File.ReadAllText(Path.Combine(raiz, "EasyStock.Api", "Services", "GeradorNotificacoesAutomaticas.cs"));

        gerador.Should().Contain("GetAlertasAutomaticosAsync", "os alertas vigentes vêm numa consulta por empresa, não uma por item");
        gerador.Should().Contain("ExpurgarAlertasAutomaticosAsync", "a retenção roda na própria rodada do job");
        gerador.Should().Contain("RemoverAlertasAsync", "a condição resolvida sai do sino");
        gerador.Should().Contain("apenasComSaldo: true", "lote sem saldo não alerta");

        var provas = File.ReadAllText(Path.Combine(
            raiz, "EasyStock.Infra.Postgre.IntegrationTests", "Tenancy", "AlertasDeEstoquePorEmpresaIntegrationTests.cs"));
        provas.Should().Contain("Condicao_que_persiste_gera_um_alerta_em_dias_seguidos", "a deduplicação precisa de prova em Postgres");
        provas.Should().Contain("Retencao_expurga_so_o_alerta_automatico", "a retenção precisa de prova em Postgres");
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
