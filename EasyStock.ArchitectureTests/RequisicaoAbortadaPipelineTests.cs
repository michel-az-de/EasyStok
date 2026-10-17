using FluentAssertions;

namespace EasyStock.ArchitectureTests;

/// <summary>
/// Guard (#1874): o <c>RequisicaoAbortadaMiddleware</c> tem de estar ligado no pipeline da Api, logo abaixo
/// do log de requisição do Serilog (para ele registrar o 499) e antes da autenticação (para pegar o
/// cancelamento de tudo que vem depois). Os testes unitários provam a regra; este prova a fiação: sem a
/// linha no pipeline eles seguem verdes e a requisição abortada volta a sair como Error "responded 500".
///
/// É leitura de texto porque a Api só sobe com Postgres, e o projeto de integração dela não roda no CI.
/// </summary>
[Trait("Category", "Architecture")]
public class RequisicaoAbortadaPipelineTests
{
    private const string PipelinePath = "EasyStock.Api/Hosting/PipelineExtensions.cs";

    [Fact]
    public void Middleware_De_Requisicao_Abortada_Deve_Ficar_Entre_O_Log_De_Requisicao_E_A_Autenticacao()
    {
        var path = Path.Combine(RepoPaths.FindRepoRoot(), PipelinePath.Replace('/', Path.DirectorySeparatorChar));
        var linhas = File.ReadAllLines(path)
            .Select(l => l.Trim())
            .Where(l => !l.StartsWith("//"))
            .ToList();

        var log = linhas.FindIndex(l => l.StartsWith("app.UseSerilogRequestLogging("));
        var abortada = linhas.FindIndex(l => l == "app.UseMiddleware<RequisicaoAbortadaMiddleware>();");
        var autenticacao = linhas.FindIndex(l => l == "app.UseAuthentication();");

        log.Should().BeGreaterThanOrEqualTo(0, "UseSerilogRequestLogging sumiu de " + PipelinePath + "; atualize este teste.");
        autenticacao.Should().BeGreaterThanOrEqualTo(0, "UseAuthentication sumiu de " + PipelinePath + "; atualize este teste.");
        abortada.Should().BeGreaterThanOrEqualTo(0,
            "sem o middleware no pipeline, a requisição abortada pelo cliente volta a ser logada como Error 500 (#1874).");
        abortada.Should().BeGreaterThan(log, "abaixo do log de requisição, ele registra o 499 em vez de ver a exceção passar.");
        abortada.Should().BeLessThan(autenticacao, "antes da autenticação, ele cobre o cancelamento de tudo que vem depois.");
    }
}
