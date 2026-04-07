using EasyStock.Domain.ValueObjects;
using FluentAssertions;

namespace EasyStock.Domain.Tests.ValueObjects;

public class CodigoSkuTests
{
    [Fact]
    public void Deve_criar_codigo_sku_quando_valido()
    {
        // Arrange
        var value = "ABC-123";

        // Act
        var sku = CodigoSku.From(value);

        // Assert
        sku.Value.Should().Be("ABC-123");
    }

    [Fact]
    public void Deve_normalizar_para_maiusculo()
    {
        // Arrange
        var value = "abc-123";

        // Act
        var sku = CodigoSku.From(value);

        // Assert
        sku.Value.Should().Be("ABC-123");
    }

    [Fact]
    public void Deve_trim_whitespace()
    {
        // Arrange
        var value = "  ABC-123  ";

        // Act
        var sku = CodigoSku.From(value);

        // Assert
        sku.Value.Should().Be("ABC-123");
    }

    [Fact]
    public void Nao_deve_permitir_sku_vazio()
    {
        // Arrange
        var value = "";

        // Act
        Action act = () => CodigoSku.From(value);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*SKU é obrigatório.*");
    }

    [Fact]
    public void Nao_deve_permitir_sku_whitespace()
    {
        // Arrange
        var value = "   ";

        // Act
        Action act = () => CodigoSku.From(value);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*SKU é obrigatório.*");
    }

    [Fact]
    public void Nao_deve_permitir_sku_muito_longo()
    {
        // Arrange
        var value = new string('A', 101);

        // Act
        Action act = () => CodigoSku.From(value);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*SKU muito longo.*");
    }

    [Fact]
    public void Nao_deve_permitir_caracteres_invalidos()
    {
        // Arrange
        var value = "ABC@123";

        // Act
        Action act = () => CodigoSku.From(value);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*SKU contém caracteres inválidos.*");
    }

    [Fact]
    public void Deve_permitir_letras_digitos_traco_underline()
    {
        // Arrange
        var value = "A1_B-2";

        // Act
        var sku = CodigoSku.From(value);

        // Assert
        sku.Value.Should().Be("A1_B-2");
    }

    [Fact]
    public void Deve_ser_igual_por_valor()
    {
        // Arrange
        var s1 = CodigoSku.From("ABC-123");
        var s2 = CodigoSku.From("ABC-123");

        // Act & Assert
        s1.Should().Be(s2);
    }

    [Fact]
    public void Deve_ser_diferente_por_valor()
    {
        // Arrange
        var s1 = CodigoSku.From("ABC-123");
        var s2 = CodigoSku.From("DEF-456");

        // Act & Assert
        s1.Should().NotBe(s2);
    }

    [Fact]
    public void Deve_retornar_string_do_valor()
    {
        // Arrange
        var sku = CodigoSku.From("ABC-123");

        // Act
        var str = sku.ToString();

        // Assert
        str.Should().Be("ABC-123");
    }
}