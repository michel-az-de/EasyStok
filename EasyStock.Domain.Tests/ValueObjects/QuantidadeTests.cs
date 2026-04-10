using EasyStock.Domain.ValueObjects;
using FluentAssertions;

namespace EasyStock.Domain.Tests.ValueObjects;

public class QuantidadeTests
{
    [Fact]
    public void Deve_criar_quantidade_quando_valor_for_valido()
    {
        var valor = 10;

        var quantidade = Quantidade.From(valor);

        quantidade.Value.Should().Be(10);
    }

    [Fact]
    public void Nao_deve_permitir_valor_negativo()
    {
        var valor = -1;

        Action act = () => Quantidade.From(valor);

        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithMessage("*Quantidade não pode ser negativa.*");
    }

    [Fact]
    public void Deve_retornar_zero_para_quantidade_zero()
    {
        var quantidade = Quantidade.Zero;

        quantidade.Value.Should().Be(0);
    }

    [Fact]
    public void Deve_adicionar_quantidade_corretamente()
    {
        var q1 = Quantidade.From(10);
        var q2 = Quantidade.From(5);

        var resultado = q1.Add(q2);

        resultado.Value.Should().Be(15);
    }

    [Fact]
    public void Deve_subtrair_quantidade_quando_resultado_nao_for_negativo()
    {
        var q1 = Quantidade.From(10);
        var q2 = Quantidade.From(5);

        var resultado = q1.Subtract(q2);

        resultado.Value.Should().Be(5);
    }

    [Fact]
    public void Nao_deve_subtrair_quantidade_quando_resultado_for_negativo()
    {
        var q1 = Quantidade.From(5);
        var q2 = Quantidade.From(10);

        Action act = () => q1.Subtract(q2);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Resultado da subtração resultaria em quantidade negativa.");
    }

    [Fact]
    public void Deve_ser_igual_por_valor()
    {
        var q1 = Quantidade.From(10);
        var q2 = Quantidade.From(10);

        q1.Should().Be(q2);
    }

    [Fact]
    public void Deve_ser_diferente_por_valor()
    {
        var q1 = Quantidade.From(10);
        var q2 = Quantidade.From(11);

        q1.Should().NotBe(q2);
    }

    [Fact]
    public void Deve_retornar_string_do_valor()
    {
        var quantidade = Quantidade.From(10);

        var str = quantidade.ToString();

        str.Should().Be("10");
    }
}