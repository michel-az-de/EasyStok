using System.Text.RegularExpressions;
using FluentAssertions;

namespace EasyStock.ArchitectureTests;

/// <summary>
/// O canhoto da cliente (PNG do resumo do pedido) guarda as cores da marca num arquivo só, <c>MarcaDaCasa.cs</c> (#2954).
/// As cores que o Console compartilha não podem divergir do <c>tokens.css</c>. Creme, Linha e os tons de texto
/// (Cacau, Cacau suave) divergem do Console por decisão do dono e ficam fora da comparação.
/// </summary>
[Trait("Category", "Architecture")]
public class MarcaCanhotoTokensTests
{
    private static readonly string[] Pdf = ["EasyStock.Infra.Async", "Pdf"];

    [Theory]
    [InlineData("Papel", "papel")]
    [InlineData("Marca", "marca")]
    [InlineData("MarcaForte", "marca-forte")]
    [InlineData("MarcaClaro", "marca-claro")]
    [InlineData("Trigo", "trigo")]
    public void MarcaDaCasa_NaoDiverge_DoTokensCss_NasCoresCompartilhadas(string constante, string token)
    {
        var marca = ArchTestPaths.ReadAppFile(Pdf[0], Pdf[1], "MarcaDaCasa.cs");
        var tokens = ArchTestPaths.ReadAppFile("EasyStock.Console", "src", "estilos", "tokens.css");

        var noCanhoto = Regex.Match(marca, $@"const string {constante}\s*=\s*""(#[0-9A-Fa-f]{{6}})""");
        // Valor claro: o primeiro de light-dark(claro, escuro), ou o valor simples.
        var noConsole = Regex.Match(tokens, $@"--{token}:\s*(?:light-dark\()?(#[0-9A-Fa-f]{{6}})");

        noCanhoto.Success.Should().BeTrue($"MarcaDaCasa deve declarar {constante}");
        noConsole.Success.Should().BeTrue($"tokens.css deve declarar --{token}");
        noCanhoto.Groups[1].Value.ToUpperInvariant().Should().Be(noConsole.Groups[1].Value.ToUpperInvariant());
    }

    [Fact]
    public void ResumoPedidoRenderer_NaoTemHexLiteral_LeDeMarcaDaCasa()
    {
        var fonte = ArchTestPaths.ReadAppFile(Pdf[0], Pdf[1], "ResumoPedidoRenderer.cs");

        Regex.Matches(fonte, @"#[0-9A-Fa-f]{6}\b").Select(m => m.Value).Should().BeEmpty();
    }
}
