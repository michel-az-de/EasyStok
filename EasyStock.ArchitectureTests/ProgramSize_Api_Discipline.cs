using FluentAssertions;

namespace EasyStock.ArchitectureTests;

/// <summary>
/// Trava regressão F3 (god Program.cs da Api).
///
/// O alvo de F3 é <c>EasyStock.Api/Program.cs</c> com até <see cref="Alvo"/> linhas (eram 1037). #2371: era
/// <c>ArchitectureDebt</c>, que o CI filtra, então nada barrava o arquivo de crescer. Agora é ratchet: o teto é o
/// tamanho medido (245 em 2026-11-15) e só desce. Passar do teto falha; ficar abaixo dele também falha, pedindo
/// para baixar o <see cref="Teto"/> ao número novo. Quando o teto chegar ao alvo, o ratchet acaba.
///
/// Linhas físicas, inclui brancos e comentários: <c>File.ReadAllLines(path).Length</c>.
/// </summary>
[Trait("Category", "Architecture")]
public class ProgramSize_Api_Discipline
{
    // Barra normal: o CI roda em Linux, onde a barra invertida não é separador.
    private const string TargetRelativePath = "EasyStock.Api/Program.cs";
    private const int Alvo = 200;
    private const int Teto = 245;

    private static int LinhasDoProgram()
    {
        var targetPath = Path.Combine(RepoPaths.FindRepoRoot(), TargetRelativePath);
        File.Exists(targetPath).Should().BeTrue(
            $"alvo {targetPath} sumiu. Program.cs foi movido? Atualize este teste.");
        return File.ReadAllLines(targetPath).Length;
    }

    [Fact]
    public void Api_ProgramCs_NaoPassaDoTeto()
    {
        var linhas = LinhasDoProgram();

        linhas.Should().BeLessThanOrEqualTo(Teto,
            $"EasyStock.Api/Program.cs concentra responsabilidades demais ({linhas} linhas, teto {Teto}). " +
            $"Registre o serviço numa extension Add*() em vez de crescer o Program (F3, alvo {Alvo}).");
    }

    [Fact]
    public void Teto_AcompanhaOProgramQuandoEleEncolhe()
    {
        var linhas = LinhasDoProgram();

        (linhas < Teto && Teto > Alvo).Should().BeFalse(
            $"o Program.cs encolheu para {linhas} linhas. Baixe o Teto para {Math.Max(linhas, Alvo)} " +
            "para o ratchet não devolver a folga.");
    }
}
