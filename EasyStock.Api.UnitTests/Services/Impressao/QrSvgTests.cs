using System.Text.RegularExpressions;
using EasyStock.Api.Services.Impressao;
using FluentAssertions;

namespace EasyStock.Api.UnitTests.Services.Impressao;

/// <summary>#1632: o QR dos impressos sai em SVG de módulos inteiros, com a zona de silêncio dentro do viewBox.</summary>
public class QrSvgTests
{
    [Fact]
    public void LinkCurtoCabeNaVersao3ComZonaDeSilencio()
    {
        var svg = QrSvg.Gerar("https://wa.me/5511900000000?text=Pedido%20A7F3C21B");

        var lado = int.Parse(Regex.Match(svg, "viewBox=\"0 0 (\\d+) ").Groups[1].Value);
        // Versão v tem 17 + 4v módulos; mais 4 de silêncio de cada lado.
        ((lado - 8 - 17) % 4).Should().Be(0);
        lado.Should().BeLessThanOrEqualTo(41 + 8, "QR pequeno o bastante para módulo de 0,5 mm em 23 mm");
        svg.Should().StartWith("<svg class=\"qr\"").And.Contain("shape-rendering=\"crispEdges\"").And.Contain("fill=\"#000\"");
    }

    [Fact]
    public void ZonaDeSilencioFicaVaziaEOsTresCantosTemLocalizador()
    {
        var svg = QrSvg.Gerar("A7F3C21B");
        var lado = int.Parse(Regex.Match(svg, "viewBox=\"0 0 (\\d+) ").Groups[1].Value);
        var modulos = Regex.Matches(svg, "M(\\d+) (\\d+)h1v1h-1z")
            .Select(m => (X: int.Parse(m.Groups[1].Value), Y: int.Parse(m.Groups[2].Value)))
            .ToHashSet();

        modulos.Should().OnlyContain(m => m.X >= 4 && m.Y >= 4 && m.X < lado - 4 && m.Y < lado - 4);
        modulos.Should().Contain([(4, 4), (lado - 5, 4), (4, lado - 5)]);
        modulos.Should().NotContain((lado - 5, lado - 5), "o quarto canto não tem localizador");
    }

    [Fact]
    public void MesmoConteudoMesmoDesenho()
    {
        QrSvg.Gerar("A7F3C21B").Should().Be(QrSvg.Gerar("A7F3C21B"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void ConteudoVazioLanca(string conteudo)
    {
        var act = () => QrSvg.Gerar(conteudo);

        act.Should().Throw<ArgumentException>();
    }
}
