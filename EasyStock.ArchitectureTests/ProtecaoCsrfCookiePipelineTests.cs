using FluentAssertions;

namespace EasyStock.ArchitectureTests;

/// <summary>
/// Guard (ADR-0053, issue #1088): o <c>ProtecaoCsrfCookieMiddleware</c> tem de estar ligado no
/// pipeline da Api, antes da autenticacao. Os testes unitarios provam a regra; este prova a fiacao.
/// Sem ele, tirar a linha do pipeline deixa todos os testes verdes e o storefront sem defesa.
///
/// E leitura de texto porque a Api so sobe com Postgres, e o projeto de integracao dela nao roda
/// no CI.
/// </summary>
[Trait("Category", "Architecture")]
public class ProtecaoCsrfCookiePipelineTests
{
    private const string PipelinePath = "EasyStock.Api/Hosting/PipelineExtensions.cs";

    [Fact]
    public void Middleware_De_Csrf_Do_Storefront_Deve_Rodar_Antes_Da_Autenticacao()
    {
        var path = Path.Combine(RepoPaths.FindRepoRoot(), PipelinePath.Replace('/', Path.DirectorySeparatorChar));
        var linhas = File.ReadAllLines(path)
            .Select(l => l.Trim())
            .Where(l => !l.StartsWith("//"))
            .ToList();

        var csrf = linhas.FindIndex(l => l == "app.UseMiddleware<ProtecaoCsrfCookieMiddleware>();");
        var autenticacao = linhas.FindIndex(l => l == "app.UseAuthentication();");

        csrf.Should().BeGreaterThanOrEqualTo(0,
            "sem o middleware no pipeline, POST com cookie do storefront volta a aceitar outro site (ADR-0053).");
        autenticacao.Should().BeGreaterThanOrEqualTo(0, "UseAuthentication sumiu de " + PipelinePath + "; atualize este teste.");
        csrf.Should().BeLessThan(autenticacao,
            "a recusa tem de vir antes de autenticar e de tocar sessao ou banco.");
    }
}
