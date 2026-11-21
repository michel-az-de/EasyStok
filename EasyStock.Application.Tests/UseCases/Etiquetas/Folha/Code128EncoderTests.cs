using EasyStock.Application.UseCases.Etiquetas.Folha;

namespace EasyStock.Application.Tests.UseCases.Etiquetas.Folha;

public class Code128EncoderTests
{
    [Fact]
    public void DigitosEmQuantidadePar_UsaConjuntoC_ComVerificadorCorreto()
    {
        // 105 + 12·1 + 34·2 + 56·3 + 78·4 = 665; 665 mod 103 = 47
        var codigo = Code128.Codificar("12345678");

        codigo.Conjunto.Should().Be('C');
        codigo.Simbolos.Should().Equal(105, 12, 34, 56, 78, 47, 106);
        codigo.Verificador.Should().Be(47);
        codigo.Modulos.Should().HaveLength(79);
        codigo.Modulos.Should().StartWith("11010011100", "padrão do Start C (211232)");
        codigo.Modulos.Should().EndWith("1100011101011", "padrão do Stop (2331112)");
    }

    [Fact]
    public void CodigoDoLote_16Digitos_Da123Modulos()
    {
        var codigo = Code128.Codificar("2611037070820001");

        codigo.Conjunto.Should().Be('C');
        codigo.Modulos.Should().HaveLength(123, "(8 pares + início + verificador) x 11 + parada 13");
    }

    [Fact]
    public void Texto_UsaConjuntoB_ComVerificadorCorreto()
    {
        // 104 + 48·1 + 42·2 + 42·3 + 17·4 + 18·5 + 19·6 + 35·7 = 879; 879 mod 103 = 55
        var codigo = Code128.Codificar("PJJ123C");

        codigo.Conjunto.Should().Be('B');
        codigo.Simbolos[0].Should().Be(104);
        codigo.Verificador.Should().Be(55);
        codigo.Modulos.Should().HaveLength((7 + 2) * 11 + 13);
    }

    [Theory]
    [InlineData("123")]
    [InlineData("12a4")]
    public void DigitosImparesOuMisturados_UsaConjuntoB(string valor)
    {
        Code128.Codificar(valor).Conjunto.Should().Be('B');
    }

    [Theory]
    [InlineData("")]
    [InlineData("Validade ç")]
    [InlineData("linha\nquebrada")]
    public void ForaDoAscii_Recusa(string valor)
    {
        Code128.Problema(valor).Should().NotBeNull();
        var codificar = () => Code128.Codificar(valor);
        codificar.Should().Throw<ArgumentException>();
    }
}
