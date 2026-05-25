using EasyStock.Domain.ValueObjects;
using FluentAssertions;

namespace EasyStock.Domain.Tests.ValueObjects;

public class DimensoesTests
{
    [Fact]
    public void Deve_criar_dimensoes_quando_valores_validos()
    {
        var peso = 1.234m;
        var largura = 10.55m;
        var altura = 5.67m;
        var comprimento = 20.89m;

        var dimensoes = Dimensoes.From(peso, largura, altura, comprimento);

        dimensoes.Peso.Should().Be(1.234m);
        dimensoes.Largura.Should().Be(10.55m);
        dimensoes.Altura.Should().Be(5.67m);
        dimensoes.Comprimento.Should().Be(20.89m);
    }

    [Fact]
    public void Deve_arredondar_peso_para_tres_casas()
    {
        var peso = 1.23456m;

        var dimensoes = Dimensoes.From(peso, 10, 5, 20);

        dimensoes.Peso.Should().Be(1.235m);
    }

    [Fact]
    public void Deve_arredondar_dimensoes_para_duas_casas()
    {
        var largura = 10.555m;

        var dimensoes = Dimensoes.From(1, largura, 5, 20);

        dimensoes.Largura.Should().Be(10.56m);
    }

    [Theory]
    [InlineData(-1, 10, 5, 20, "Peso")]
    [InlineData(1, -10, 5, 20, "Largura")]
    [InlineData(1, 10, -5, 20, "Altura")]
    [InlineData(1, 10, 5, -20, "Comprimento")]
    public void Nao_deve_permitir_valores_negativos(decimal peso, decimal largura, decimal altura, decimal comprimento, string campo)
    {
        Action act = () => Dimensoes.From(peso, largura, altura, comprimento);

        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithMessage($"*{campo} nao pode ser negativa.*");
    }

    [Fact]
    public void Deve_estar_vazio_quando_todos_zero()
    {
        var dimensoes = Dimensoes.From(0, 0, 0, 0);

        var vazio = dimensoes.EstaVazio();

        vazio.Should().BeTrue();
    }

    [Fact]
    public void Nao_deve_estar_vazio_quando_algum_nao_zero()
    {
        var dimensoes = Dimensoes.From(1, 0, 0, 0);

        var vazio = dimensoes.EstaVazio();

        vazio.Should().BeFalse();
    }

    [Fact]
    public void Deve_ser_igual_por_valores()
    {
        var d1 = Dimensoes.From(1, 10, 5, 20);
        var d2 = Dimensoes.From(1, 10, 5, 20);

        d1.Should().Be(d2);
    }

    [Fact]
    public void Deve_ser_diferente_por_valores()
    {
        var d1 = Dimensoes.From(1, 10, 5, 20);
        var d2 = Dimensoes.From(1, 10, 5, 21);

        d1.Should().NotBe(d2);
    }

    [Fact]
    public void Deve_retornar_string_formatada()
    {
        var dimensoes = Dimensoes.From(1.234m, 10.55m, 5.67m, 20.89m);

        var str = dimensoes.ToString();

        str.Should().Be("P:1.234 L:10.55 A:5.67 C:20.89");
    }
}
