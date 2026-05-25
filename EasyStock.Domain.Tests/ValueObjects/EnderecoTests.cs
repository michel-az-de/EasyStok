using EasyStock.Domain.ValueObjects;
using FluentAssertions;

namespace EasyStock.Domain.Tests.ValueObjects;

public class EnderecoTests
{
    [Fact]
    public void Construtor_aceita_todos_os_campos_opcionais_e_aplica_default_BR_em_pais()
    {
        var endereco = new Endereco();

        endereco.Logradouro.Should().BeNull();
        endereco.Numero.Should().BeNull();
        endereco.Complemento.Should().BeNull();
        endereco.Bairro.Should().BeNull();
        endereco.Cidade.Should().BeNull();
        endereco.Uf.Should().BeNull();
        endereco.Cep.Should().BeNull();
        endereco.Pais.Should().Be("BR");
    }

    [Fact]
    public void Construtor_preserva_valores_fornecidos()
    {
        var endereco = new Endereco(
            Logradouro: "Av. Paulista",
            Numero: "1000",
            Complemento: "Sala 1",
            Bairro: "Bela Vista",
            Cidade: "Sao Paulo",
            Uf: "SP",
            Cep: "01310-100",
            Pais: "BR");

        endereco.Logradouro.Should().Be("Av. Paulista");
        endereco.Numero.Should().Be("1000");
        endereco.Complemento.Should().Be("Sala 1");
        endereco.Bairro.Should().Be("Bela Vista");
        endereco.Cidade.Should().Be("Sao Paulo");
        endereco.Uf.Should().Be("SP");
        endereco.Cep.Should().Be("01310-100");
        endereco.Pais.Should().Be("BR");
    }

    [Fact]
    public void Equality_e_estrutural_para_records()
    {
        var a = new Endereco("Rua A", "10", null, "Centro", "RJ", "RJ", "20000-000");
        var b = new Endereco("Rua A", "10", null, "Centro", "RJ", "RJ", "20000-000");

        a.Should().Be(b);
        a.GetHashCode().Should().Be(b.GetHashCode());
    }

    [Fact]
    public void Equality_diferencia_quando_um_campo_diverge()
    {
        var a = new Endereco("Rua A", "10");
        var b = new Endereco("Rua A", "11");

        a.Should().NotBe(b);
    }

    [Fact]
    public void Pais_pode_ser_sobrescrito()
    {
        var endereco = new Endereco(Pais: "PT");
        endereco.Pais.Should().Be("PT");
    }
}
