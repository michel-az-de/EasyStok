using EasyStock.Domain.Exceptions;
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

        act.Should().Throw<RegraDeDominioVioladaException>()
            .WithMessage($"*{campo} não pode ser negativ*");
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

    // ─── #688/BUG-010a: coerência de dimensões lineares no write path ───

    [Fact]
    public void From_continua_leniente_para_nao_quebrar_leitura_de_legado()
    {
        // 10 x 0 x 0 é incoerente, mas From() NÃO lança (preserva desserialização de legado).
        var act = () => Dimensoes.From(0, 10, 0, 0);
        act.Should().NotThrow();
    }

    [Theory]
    [InlineData(0, 10, 0, 0)]   // só largura
    [InlineData(0, 10, 5, 0)]   // largura + altura, sem comprimento
    [InlineData(2, 0, 5, 0)]    // peso + altura, lineares parciais
    public void EnsureCoerente_rejeita_dimensoes_lineares_parciais(decimal peso, decimal largura, decimal altura, decimal comprimento)
    {
        var dimensoes = Dimensoes.From(peso, largura, altura, comprimento);

        var act = () => dimensoes.EnsureCoerente();

        act.Should().Throw<RegraDeDominioVioladaException>()
            .WithMessage("*incompletas*");
    }

    [Theory]
    [InlineData(0, 0, 0, 0)]      // sem caixa (tudo zero) — válido
    [InlineData(10, 0, 0, 0)]     // só peso, sem caixa — válido
    [InlineData(1, 10, 5, 20)]    // caixa completa — válido
    public void EnsureCoerente_aceita_vazio_so_peso_ou_caixa_completa(decimal peso, decimal largura, decimal altura, decimal comprimento)
    {
        var dimensoes = Dimensoes.From(peso, largura, altura, comprimento);

        var act = () => dimensoes.EnsureCoerente();

        act.Should().NotThrow();
    }
}