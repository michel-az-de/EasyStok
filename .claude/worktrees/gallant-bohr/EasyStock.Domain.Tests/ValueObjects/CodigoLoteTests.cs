using EasyStock.Domain.ValueObjects;
using FluentAssertions;

namespace EasyStock.Domain.Tests.ValueObjects;

public class CodigoLoteTests
{
    [Fact]
    public void Deve_criar_codigo_lote_quando_valido()
    {
        // Arrange
        var value = "LOT-001/2024";

        // Act
        var lote = CodigoLote.From(value);

        // Assert
        lote.Value.Should().Be("LOT-001/2024");
    }

    [Fact]
    public void Deve_normalizar_para_maiusculo()
    {
        // Arrange
        var value = "lot-001/2024";

        // Act
        var lote = CodigoLote.From(value);

        // Assert
        lote.Value.Should().Be("LOT-001/2024");
    }

    [Fact]
    public void Deve_trim_whitespace()
    {
        // Arrange
        var value = "  LOT-001  ";

        // Act
        var lote = CodigoLote.From(value);

        // Assert
        lote.Value.Should().Be("LOT-001");
    }

    [Fact]
    public void Nao_deve_permitir_codigo_vazio()
    {
        // Arrange
        var value = "";

        // Act
        Action act = () => CodigoLote.From(value);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*Codigo de lote e obrigatorio.*");
    }

    [Fact]
    public void Nao_deve_permitir_codigo_muito_longo()
    {
        // Arrange
        var value = new string('A', 101);

        // Act
        Action act = () => CodigoLote.From(value);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*Codigo de lote muito longo.*");
    }

    [Fact]
    public void Nao_deve_permitir_caracteres_invalidos()
    {
        // Arrange
        var value = "LOT@001";

        // Act
        Action act = () => CodigoLote.From(value);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*Codigo de lote contem caracteres invalidos.*");
    }

    [Fact]
    public void Deve_permitir_letras_digitos_traco_underline_barra()
    {
        // Arrange
        var value = "L1_T-1/24";

        // Act
        var lote = CodigoLote.From(value);

        // Assert
        lote.Value.Should().Be("L1_T-1/24");
    }

    [Fact]
    public void Deve_ser_igual_por_valor()
    {
        // Arrange
        var l1 = CodigoLote.From("LOT-001");
        var l2 = CodigoLote.From("LOT-001");

        // Act & Assert
        l1.Should().Be(l2);
    }

    [Fact]
    public void Deve_ser_diferente_por_valor()
    {
        // Arrange
        var l1 = CodigoLote.From("LOT-001");
        var l2 = CodigoLote.From("LOT-002");

        // Act & Assert
        l1.Should().NotBe(l2);
    }

    [Fact]
    public void Deve_retornar_string_do_valor()
    {
        // Arrange
        var lote = CodigoLote.From("LOT-001");

        // Act
        var str = lote.ToString();

        // Assert
        str.Should().Be("LOT-001");
    }
}