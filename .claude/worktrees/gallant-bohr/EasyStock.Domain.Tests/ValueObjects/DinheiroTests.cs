using EasyStock.Domain.ValueObjects;
using FluentAssertions;

namespace EasyStock.Domain.Tests.ValueObjects;

public class DinheiroTests
{
    [Fact]
    public void Deve_criar_dinheiro_quando_valor_for_valido()
    {
        // Arrange
        var valor = 10.50m;

        // Act
        var dinheiro = Dinheiro.FromDecimal(valor);

        // Assert
        dinheiro.Valor.Should().Be(10.50m);
    }

    [Fact]
    public void Deve_arredondar_para_duas_casas_decimais()
    {
        // Arrange
        var valor = 10.555m;

        // Act
        var dinheiro = Dinheiro.FromDecimal(valor);

        // Assert
        dinheiro.Valor.Should().Be(10.56m);
    }

    [Fact]
    public void Nao_deve_permitir_valor_negativo()
    {
        // Arrange
        var valor = -1.00m;

        // Act
        Action act = () => Dinheiro.FromDecimal(valor);

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithMessage("*Valor monetário não pode ser negativo.*");
    }

    [Fact]
    public void Deve_retornar_zero_para_dinheiro_zero()
    {
        // Act
        var dinheiro = Dinheiro.Zero;

        // Assert
        dinheiro.Valor.Should().Be(0m);
    }

    [Fact]
    public void Deve_adicionar_dinheiro_corretamente()
    {
        // Arrange
        var dinheiro1 = Dinheiro.FromDecimal(10.00m);
        var dinheiro2 = Dinheiro.FromDecimal(5.50m);

        // Act
        var resultado = dinheiro1.Add(dinheiro2);

        // Assert
        resultado.Valor.Should().Be(15.50m);
    }

    [Fact]
    public void Deve_subtrair_dinheiro_quando_resultado_nao_for_negativo()
    {
        // Arrange
        var dinheiro1 = Dinheiro.FromDecimal(10.00m);
        var dinheiro2 = Dinheiro.FromDecimal(5.50m);

        // Act
        var resultado = dinheiro1.Subtract(dinheiro2);

        // Assert
        resultado.Valor.Should().Be(4.50m);
    }

    [Fact]
    public void Nao_deve_subtrair_dinheiro_quando_resultado_for_negativo()
    {
        // Arrange
        var dinheiro1 = Dinheiro.FromDecimal(5.00m);
        var dinheiro2 = Dinheiro.FromDecimal(10.00m);

        // Act
        Action act = () => dinheiro1.Subtract(dinheiro2);

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Operação resultaria em valor monetário negativo.");
    }

    [Fact]
    public void Deve_ser_igual_por_valor()
    {
        // Arrange
        var dinheiro1 = Dinheiro.FromDecimal(10.00m);
        var dinheiro2 = Dinheiro.FromDecimal(10.00m);

        // Act & Assert
        dinheiro1.Should().Be(dinheiro2);
        dinheiro1.GetHashCode().Should().Be(dinheiro2.GetHashCode());
    }

    [Fact]
    public void Deve_ser_diferente_por_valor()
    {
        // Arrange
        var dinheiro1 = Dinheiro.FromDecimal(10.00m);
        var dinheiro2 = Dinheiro.FromDecimal(10.01m);

        // Act & Assert
        dinheiro1.Should().NotBe(dinheiro2);
    }

    [Fact]
    public void Deve_retornar_string_formatada()
    {
        // Arrange
        var dinheiro = Dinheiro.FromDecimal(10.50m);

        // Act
        var str = dinheiro.ToString();

        // Assert
        str.Should().Be("10.50");
    }
}