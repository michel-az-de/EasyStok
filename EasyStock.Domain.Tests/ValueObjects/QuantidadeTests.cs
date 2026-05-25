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

    // Testes para suporte a quantidades fracionárias (PR-A: int → decimal)

    [Fact]
    public void Deve_criar_quantidade_fracionaria_quando_valor_for_valido()
    {
        var quantidade = Quantidade.From(0.5m);

        quantidade.Value.Should().Be(0.5m);
    }

    [Fact]
    public void Deve_criar_quantidade_com_tres_casas_decimais()
    {
        var quantidade = Quantidade.From(1.001m);

        quantidade.Value.Should().Be(1.001m);
    }

    [Fact]
    public void Deve_adicionar_quantidades_fracionarias_corretamente()
    {
        var q1 = Quantidade.From(1.5m);
        var q2 = Quantidade.From(0.5m);

        var resultado = q1.Add(q2);

        resultado.Value.Should().Be(2.0m);
    }

    [Fact]
    public void Deve_subtrair_quantidades_fracionarias_corretamente()
    {
        var q1 = Quantidade.From(1.5m);
        var q2 = Quantidade.From(0.5m);

        var resultado = q1.Subtract(q2);

        resultado.Value.Should().Be(1.0m);
    }

    [Fact]
    public void Nao_deve_permitir_valor_fracionario_negativo()
    {
        Action act = () => Quantidade.From(-0.001m);

        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithMessage("*Quantidade não pode ser negativa.*");
    }

    [Fact]
    public void Deve_retornar_string_do_valor_decimal()
    {
        var quantidade = Quantidade.From(1.5m);

        quantidade.ToString().Should().Be("1.5");
    }

    [Fact]
    public void Deve_converter_implicitamente_para_decimal()
    {
        Quantidade quantidade = Quantidade.From(2.75m);

        decimal valor = quantidade;

        valor.Should().Be(2.75m);
    }

    [Fact]
    public void Deve_manter_compatibilidade_com_int_via_from_overload()
    {
        var quantidade = Quantidade.From(10);

        quantidade.Value.Should().Be(10m);
    }

    [Fact]
    public void Deve_manter_compatibilidade_com_conversao_implicita_para_int()
    {
        var quantidade = Quantidade.From(5);

        int valor = quantidade;

        valor.Should().Be(5);
    }
}
