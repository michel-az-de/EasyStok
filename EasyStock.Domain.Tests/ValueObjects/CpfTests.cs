using EasyStock.Domain.ValueObjects;
using FluentAssertions;

namespace EasyStock.Domain.Tests.ValueObjects;

public class CpfTests
{
    [Theory]
    [InlineData("111.444.777-35")]   // com máscara
    [InlineData("11144477735")]      // só dígitos
    [InlineData("123.456.789-09")]
    [InlineData("529.982.247-25")]
    public void EhValido_aceita_cpf_com_digito_verificador_correto(string input)
    {
        Cpf.EhValido(input).Should().BeTrue();
    }

    [Theory]
    [InlineData("11111111111")]      // todos iguais (passa na conta, é inválido)
    [InlineData("00000000000")]      // todos zero
    [InlineData("12345678900")]      // dígito verificador errado
    [InlineData("123.456.789-00")]
    public void EhValido_rejeita_cpf_invalido(string input)
    {
        Cpf.EhValido(input).Should().BeFalse();
    }

    [Theory]
    [InlineData("123")]              // curto demais
    [InlineData("1234567890")]       // 10 dígitos
    [InlineData("12345678901234")]   // 14 dígitos (CNPJ — não é forma de CPF)
    [InlineData("")]
    [InlineData(null)]
    public void EhValido_rejeita_comprimento_diferente_de_11(string? input)
    {
        Cpf.EhValido(input).Should().BeFalse();
    }

    [Theory]
    [InlineData("111.444.777-35", true)]
    [InlineData("11144477735", true)]
    [InlineData("12345678901234", false)]   // CNPJ
    [InlineData("estrangeiro-X9", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void TemFormaDeCpf_true_apenas_para_11_digitos(string? input, bool esperado)
    {
        Cpf.TemFormaDeCpf(input).Should().Be(esperado);
    }

    [Fact]
    public void From_normaliza_para_somente_digitos()
    {
        Cpf.From("111.444.777-35").Value.Should().Be("11144477735");
    }

    [Theory]
    [InlineData("11111111111")]
    [InlineData("12345678900")]
    [InlineData("123")]
    [InlineData("")]
    public void From_lanca_para_cpf_invalido(string input)
    {
        var act = () => Cpf.From(input);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void TryFrom_retorna_null_para_invalido_ou_nulo()
    {
        Cpf.TryFrom(null).Should().BeNull();
        Cpf.TryFrom("").Should().BeNull();
        Cpf.TryFrom("11111111111").Should().BeNull();
    }

    [Fact]
    public void Formatado_retorna_cpf_com_mascara()
    {
        Cpf.From("11144477735").Formatado().Should().Be("111.444.777-35");
    }

    [Fact]
    public void Implicit_operator_converte_para_string()
    {
        var cpf = Cpf.From("11144477735");
        string str = cpf;
        str.Should().Be("11144477735");
    }
}
