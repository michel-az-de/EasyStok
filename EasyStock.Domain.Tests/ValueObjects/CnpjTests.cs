using EasyStock.Domain.ValueObjects;
using FluentAssertions;

namespace EasyStock.Domain.Tests.ValueObjects;

public class CnpjTests
{
    [Theory]
    [InlineData("11.222.333/0001-81", "11222333000181")]
    [InlineData("11222333000181",      "11222333000181")]
    [InlineData("123.456.789-09",      "12345678909")]
    [InlineData("12345678909",          "12345678909")]
    public void From_normaliza_e_armazena_apenas_digitos(string input, string expected)
    {
        Cnpj.From(input).Value.Should().Be(expected);
    }

    [Theory]
    [InlineData("123")]               // too short
    [InlineData("abcdefghijklmno")]   // non-digits
    [InlineData("")]
    public void From_rejeita_documentos_invalidos(string input)
    {
        var act = () => Cnpj.From(input);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void TryFrom_retorna_null_para_entrada_nula()
    {
        Cnpj.TryFrom(null).Should().BeNull();
        Cnpj.TryFrom("").Should().BeNull();
    }

    [Fact]
    public void Formatado_retorna_cnpj_com_mascara()
    {
        var cnpj = Cnpj.From("11222333000181");
        cnpj.Formatado().Should().Be("11.222.333/0001-81");
    }

    [Fact]
    public void Formatado_retorna_cpf_com_mascara()
    {
        var cpf = Cnpj.From("12345678909");
        cpf.Formatado().Should().Be("123.456.789-09");
    }

    [Fact]
    public void Implicit_operator_converte_para_string()
    {
        var cnpj = Cnpj.From("11222333000181");
        string str = cnpj;
        str.Should().Be("11222333000181");
    }

    [Theory]
    [InlineData("11.222.333/0001-81")]   // com máscara
    [InlineData("11222333000181")]       // só dígitos
    [InlineData("04.252.011/0001-10")]   // outro CNPJ válido
    public void EhValido_aceita_cnpj_com_digito_verificador_correto(string input)
    {
        Cnpj.EhValido(input).Should().BeTrue();
    }

    [Theory]
    [InlineData("11111111111111")]       // todos iguais — passa na conta, é inválido (caso do QA)
    [InlineData("00000000000000")]
    [InlineData("11222333000180")]       // dígito verificador errado
    [InlineData("11.222.333/0001-80")]
    public void EhValido_rejeita_cnpj_invalido(string input)
    {
        Cnpj.EhValido(input).Should().BeFalse();
    }

    [Theory]
    [InlineData("123")]                  // curto demais
    [InlineData("12345678909")]          // 11 dígitos (CPF — não é forma de CNPJ)
    [InlineData("")]
    [InlineData(null)]
    public void EhValido_rejeita_comprimento_diferente_de_14(string? input)
    {
        Cnpj.EhValido(input).Should().BeFalse();
    }

    [Theory]
    [InlineData("11.222.333/0001-81", true)]
    [InlineData("11222333000181", true)]
    [InlineData("12345678909", false)]   // CPF
    [InlineData("estrangeiro-X9", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void TemFormaDeCnpj_true_apenas_para_14_digitos(string? input, bool esperado)
    {
        Cnpj.TemFormaDeCnpj(input).Should().Be(esperado);
    }
}
