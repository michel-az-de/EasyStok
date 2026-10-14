using EasyStock.Api.Services.Impressao;
using FluentAssertions;

namespace EasyStock.Api.UnitTests.Services.Impressao;

/// <summary>S49 (#1269): o código de barras do impresso é lido pelo sistema, então tem de ser CODE128 válido.</summary>
public class Code128SvgTests
{
    /// <summary>Gerado pela python-barcode 0.15.1 (<c>Code128("A7F3C21B").build()</c>), referência independente.</summary>
    private const string ReferenciaA7F3C21B =
        "110100100001010001100011101101110100011000101100101110010001000110110011100101001110011010001011000100111001101100011101011";

    [Fact]
    public void ModulosIguaisAReferenciaIndependente()
    {
        Code128Svg.Modulos("A7F3C21B").Should().Be(ReferenciaA7F3C21B);
    }

    [Theory]
    [InlineData("A7F3C21B")]
    [InlineData("00000000")]
    [InlineData("FFFFFFFF")]
    public void DecodificaONumero(string numero)
    {
        Decodificar(Code128Svg.Modulos(numero)).Should().Be(numero);
    }

    [Fact]
    public void SvgTemZonaDeSilencioESoPreto()
    {
        var svg = Code128Svg.Gerar("A7F3C21B");

        var largura = ReferenciaA7F3C21B.Length + 2 * Code128Svg.ZonaSilencio;
        svg.Should().Contain($"viewBox=\"0 0 {largura} 40\"");
        svg.Should().Contain("<rect x=\"10\" ");
        svg.Should().NotContainAny("<script", "fill=\"#fff");
    }

    [Fact]
    public void RecusaCaractereForaDoConjuntoB()
    {
        var act = () => Code128Svg.Modulos("é");
        act.Should().Throw<ArgumentException>();
    }

    /// <summary>Decodificador mínimo do conjunto B: lê larguras de 11 módulos e confere o dígito verificador.</summary>
    private static string Decodificar(string modulos)
    {
        var larguras = new List<int>();
        var i = 0;
        while (i < modulos.Length)
        {
            var c = modulos[i];
            var n = 0;
            while (i < modulos.Length && modulos[i] == c) { n++; i++; }
            larguras.Add(n);
        }
        var tabela = typeof(Code128Svg)
            .GetField("Padroes", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .GetValue(null) as string[];
        var simbolos = new List<int>();
        for (var k = 0; k + 6 <= larguras.Count - 7; k += 6)
            simbolos.Add(Array.IndexOf(tabela!, string.Concat(larguras.Skip(k).Take(6))));
        simbolos[0].Should().Be(104, "começa com Start B");
        var dados = simbolos.Skip(1).SkipLast(1).ToList();
        var soma = 104 + dados.Select((s, j) => s * (j + 1)).Sum();
        simbolos[^1].Should().Be(soma % 103, "dígito verificador");
        return new string(dados.Select(s => (char)(s + ' ')).ToArray());
    }
}
