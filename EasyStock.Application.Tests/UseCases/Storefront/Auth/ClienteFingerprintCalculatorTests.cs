using EasyStock.Application.UseCases.Storefront.Auth;
using FluentAssertions;

namespace EasyStock.Application.Tests.UseCases.Storefront.Auth;

public class ClienteFingerprintCalculatorTests
{
    [Fact]
    public void Calcular_MesmoInput_SempreRetornaMesmoHash()
    {
        var h1 = ClienteFingerprintCalculator.Calcular("Mozilla/5.0", "pt-BR,pt;q=0.9");
        var h2 = ClienteFingerprintCalculator.Calcular("Mozilla/5.0", "pt-BR,pt;q=0.9");

        h1.Should().NotBeNullOrEmpty();
        h1.Should().Be(h2, "SHA-256 é determinístico");
    }

    [Fact]
    public void Calcular_InputDiferente_RetornaHashDiferente()
    {
        var h1 = ClienteFingerprintCalculator.Calcular("Mozilla/5.0", "pt-BR");
        var h2 = ClienteFingerprintCalculator.Calcular("Mozilla/5.0", "en-US");

        h1.Should().NotBe(h2);
    }

    [Fact]
    public void Calcular_AmbosVazios_RetornaNull()
    {
        ClienteFingerprintCalculator.Calcular(null, null).Should().BeNull();
        ClienteFingerprintCalculator.Calcular("", "").Should().BeNull();
        ClienteFingerprintCalculator.Calcular("  ", "  ").Should().BeNull();
    }

    [Fact]
    public void Calcular_ApenasUaPreenchido_RetornaHash()
    {
        var h = ClienteFingerprintCalculator.Calcular("Mozilla/5.0", null);
        h.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Calcular_RetornaHexMinusculo64Chars()
    {
        var h = ClienteFingerprintCalculator.Calcular("UA", "en");
        h.Should().HaveLength(64);
        h.Should().MatchRegex("^[0-9a-f]{64}$");
    }
}
