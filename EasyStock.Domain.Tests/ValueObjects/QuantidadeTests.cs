using EasyStock.Domain.ValueObjects;
using FluentAssertions;

namespace EasyStock.Domain.Tests.ValueObjects;

public class QuantidadeTests
{
    [Fact]
    public void Deve_criar_quantidade_quando_valor_for_valido()
    {
        // Arrange
        var valor = 10;

        // Act
        var quantidade = Quantidade.From(valor);

        // Assert
        quantidade.Value.Should().Be(10);
    }

    [Fact]
    public void Nao_deve_permitir_valor_negativo()
    {
        // Arrange
        var valor = -1;

        // Act
        Action act = () => Quantidade.From(valor);

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithMessage("*Quantidade não pode ser negativa.*");
    }

    [Fact]
    public void Deve_retornar_zero_para_quantidade_zero()
    {
        // Act
        var quantidade = Quantidade.Zero;

        // Assert
        quantidade.Value.Should().Be(0);
    }

    [Fact]
    public void Deve_adicionar_quantidade_corretamente()
    {
        // Arrange
        var q1 = Quantidade.From(10);
        var q2 = Quantidade.From(5);

        // Act
        var resultado = q1.Add(q2);

        // Assert
        resultado.Value.Should().Be(15);
    }

    [Fact]
    public void Deve_subtrair_quantidade_quando_resultado_nao_for_negativo()
    {
        // Arrange
        var q1 = Quantidade.From(10);
        var q2 = Quantidade.From(5);

        // Act
        var resultado = q1.Subtract(q2);

        // Assert
        resultado.Value.Should().Be(5);
    }

    [Fact]
    public void Nao_deve_subtrair_quantidade_quando_resultado_for_negativo()
    {
        // Arrange
        var q1 = Quantidade.From(5);
        var q2 = Quantidade.From(10);

        // Act
        Action act = () => q1.Subtract(q2);

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Resultado da subtração resultaria em quantidade negativa.");
    }

    [Fact]
    public void Deve_ser_igual_por_valor()
    {
        // Arrange
        var q1 = Quantidade.From(10);
        var q2 = Quantidade.From(10);

        // Act & Assert
        q1.Should().Be(q2);
    }

    [Fact]
    public void Deve_ser_diferente_por_valor()
    {
        // Arrange
        var q1 = Quantidade.From(10);
        var q2 = Quantidade.From(11);

        // Act & Assert
        q1.Should().NotBe(q2);
    }

    [Fact]
    public void Deve_retornar_string_do_valor()
    {
        // Arrange
        var quantidade = Quantidade.From(10);

        // Act
        var str = quantidade.ToString();

        // Assert
        str.Should().Be("10");
    }
}