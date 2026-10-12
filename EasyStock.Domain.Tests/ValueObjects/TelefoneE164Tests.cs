using EasyStock.Domain.Exceptions.Storefront;
using EasyStock.Domain.ValueObjects;
using FluentAssertions;

namespace EasyStock.Domain.Tests.ValueObjects;

/// <summary>N4: a regra E.164 BR vive num VO estrito; o <see cref="Telefone"/> frouxo aceita de 7 a 15 dígitos.</summary>
public class TelefoneE164Tests
{
    [Theory]
    [InlineData("+5511997573992", "+5511997573992")]
    [InlineData("(11) 99757-3992", "+5511997573992")]
    [InlineData("11 99757-3992", "+5511997573992")]
    [InlineData("+55 11 9 9757 3992", "+5511997573992")]
    [InlineData("1133334444", "+551133334444")]
    [InlineData("5511997573992", "+5511997573992")]
    [InlineData("551133334444", "+551133334444")]
    public void AceitaFormatosComunsENormalizaParaE164(string entrada, string esperado) =>
        TelefoneE164.From(entrada).Value.Should().Be(esperado);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    [InlineData("551199757399212")]
    [InlineData("+1 415 555 0100")]
    [InlineData("1234567")]
    public void RecusaNumeroForaDoPadraoBrasileiro(string entrada)
    {
        FluentActions.Invoking(() => TelefoneE164.From(entrada)).Should().Throw<TelefoneInvalidoException>();
        TelefoneE164.TryFrom(entrada).Should().BeNull();
    }

    [Fact]
    public void CabeNaColuna() =>
        TelefoneE164.From("+5511997573992").Value.Length.Should().BeLessThanOrEqualTo(TelefoneE164.TamanhoMaximo);
}
