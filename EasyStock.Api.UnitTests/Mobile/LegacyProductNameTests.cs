using EasyStock.Api.Mobile.Services;
using FluentAssertions;

namespace EasyStock.Api.UnitTests.Mobile;

/// <summary>#2813: o nome composto (nome + embalagem) e a chave do produto na primeira carga. O formato fica fixado aqui.</summary>
public sealed class LegacyProductNameTests
{
    [Theory]
    [InlineData("Lasanha", "600g", "Lasanha 600 g")]
    [InlineData("Lasanha", "600 g", "Lasanha 600 g")]
    [InlineData("Lasanha", "600G", "Lasanha 600 g")]
    [InlineData("Lasanha", "  600   G ", "Lasanha 600 g")]
    [InlineData("  Lasanha de frango ", "1,5kg", "Lasanha de frango 1,5 kg")]
    [InlineData("Suco", "250ML", "Suco 250 ml")]
    [InlineData("Brigadeiro", "Caixa  com 10", "Brigadeiro caixa com 10")]
    public void Compose_JuntaONomeEAEmbalagemNormalizada(string nome, string embalagem, string esperado) =>
        LegacyProductName.Compose(nome, embalagem).Should().Be(esperado);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("un")]
    [InlineData(" UN ")]
    [InlineData("und")]
    [InlineData("Unid")]
    [InlineData("unidade")]
    public void Compose_SemEmbalagemOuComUnidadeGenerica_FicaSoComONome(string? embalagem) =>
        LegacyProductName.Compose("  Pudim ", embalagem).Should().Be("Pudim");

    [Theory]
    [InlineData("Lasanha 600 g", "600g")]
    [InlineData("Lasanha 600g", "600 g")]
    [InlineData("LASANHA 600 G", "600g")]
    [InlineData("Lasanha 600 g bolonhesa", "600 g")]
    public void Compose_NaoRepeteAEmbalagemQueONomeJaTem(string nome, string embalagem) =>
        LegacyProductName.Compose(nome, embalagem).Should().Be(nome);

    [Fact]
    public void Compose_SoReconheceAEmbalagemComoPalavraInteira() =>
        LegacyProductName.Compose("Lasanha 1600 g", "600g").Should().Be("Lasanha 1600 g 600 g");

    [Fact]
    public void Compose_EEstavel_AplicarDeNovoNaoMudaONome()
    {
        var composto = LegacyProductName.Compose("Lasanha", "600g");

        LegacyProductName.Compose("Lasanha", "600g").Should().Be(composto);
        LegacyProductName.Compose(composto, "600g").Should().Be(composto);
    }

    [Fact]
    public void Compose_ComoChave_IgnoraCaixaEEspacosEDistingueEmbalagens()
    {
        var a = LegacyProductName.Compose(" lasanha", "600 G");
        var b = LegacyProductName.Compose("LASANHA ", "600g");
        var outra = LegacyProductName.Compose("Lasanha", "1kg");

        string.Equals(a, b, StringComparison.OrdinalIgnoreCase).Should().BeTrue();
        string.Equals(a, outra, StringComparison.OrdinalIgnoreCase).Should().BeFalse();
    }
}
