using System.Text.Json;
using FluentAssertions;

namespace EasyStock.ArchitectureTests;

/// <summary>
/// Guard (#3251, P2 da #3114): o <c>TravaDeAmbienteMiddleware</c> tem de estar ligado logo depois
/// do CORS, antes de arquivos, PWA e autenticação, e travado por padrão fora do desenvolvimento.
/// Os testes unitários provam a regra; este prova a fiação e a configuração. É leitura de texto
/// porque a Api só sobe com Postgres.
/// </summary>
[Trait("Category", "Architecture")]
public class TravaDeAmbientePipelineTests
{
    private const string PipelinePath = "EasyStock.Api/Hosting/PipelineExtensions.cs";
    private const string Chave = "AceitaClienteDeHomologacao";

    [Fact]
    public void Trava_De_Ambiente_Deve_Rodar_Logo_Depois_Do_Cors_E_Antes_Da_Autenticacao()
    {
        var linhas = File.ReadAllLines(Caminho(PipelinePath))
            .Select(l => l.Trim())
            .Where(l => l.Length > 0 && !l.StartsWith("//"))
            .ToList();

        var cors = linhas.FindIndex(l => l == "app.UseCors();");
        var trava = linhas.FindIndex(l => l == "app.UseMiddleware<TravaDeAmbienteMiddleware>();");
        var autenticacao = linhas.FindIndex(l => l == "app.UseAuthentication();");

        cors.Should().BeGreaterThanOrEqualTo(0, "UseCors sumiu de " + PipelinePath + "; atualize este teste.");
        trava.Should().Be(cors + 1,
            "a recusa vem logo depois do CORS (para levar a origem) e antes de PWA, login, pareamento e sync (#3251).");
        autenticacao.Should().BeGreaterThan(trava);
    }

    [Theory]
    [InlineData("EasyStock.Api/appsettings.json")]
    [InlineData("EasyStock.Api/appsettings.Production.json")]
    public void Producao_Nao_Aceita_Cliente_De_Homologacao(string arquivo)
    {
        File.ReadAllText(Caminho(arquivo)).Should().NotContain(Chave,
            "a trava fica ligada por padrão; só o desenvolvimento e o laboratório a desligam (#3251).");
    }

    [Fact]
    public void Desenvolvimento_E_Laboratorio_Aceitam_Cliente_De_Homologacao()
    {
        using var json = JsonDocument.Parse(File.ReadAllText(Caminho("EasyStock.Api/appsettings.Development.json")));
        json.RootElement.GetProperty("Ambiente").GetProperty(Chave).GetBoolean().Should().BeTrue(
            "o laboratório (scripts/homologacao) roda a Api em Development e recebe o APK de homologação.");
    }

    private static string Caminho(string relativo) =>
        Path.Combine(RepoPaths.FindRepoRoot(), relativo.Replace('/', Path.DirectorySeparatorChar));
}
