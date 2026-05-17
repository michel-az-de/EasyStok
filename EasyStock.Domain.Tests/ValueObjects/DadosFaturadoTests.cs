using EasyStock.Domain.ValueObjects;
using FluentAssertions;

namespace EasyStock.Domain.Tests.ValueObjects;

public class DadosFaturadoTests
{
    [Fact]
    public void Construtor_minimo_aceita_apenas_nome()
    {
        var faturado = new DadosFaturado(Nome: "Cliente Avulso");

        faturado.Nome.Should().Be("Cliente Avulso");
        faturado.Documento.Should().BeNull();
        faturado.Email.Should().BeNull();
        faturado.Telefone.Should().BeNull();
        faturado.Endereco.Should().BeNull();
        faturado.SchemaVersao.Should().Be(DadosFaturado.SchemaVersaoAtual);
    }

    [Fact]
    public void SchemaVersaoAtual_e_um()
    {
        DadosFaturado.SchemaVersaoAtual.Should().Be(1);
    }

    [Fact]
    public void Construtor_preserva_endereco_aninhado()
    {
        var endereco = new Endereco("Rua Cliente", "100", Cidade: "RJ", Uf: "RJ");
        var faturado = new DadosFaturado(
            Nome: "Joao",
            Documento: "12345678909",
            Email: "joao@example.com",
            Endereco: endereco);

        faturado.Endereco.Should().Be(endereco);
        faturado.Email.Should().Be("joao@example.com");
    }

    [Fact]
    public void Equality_e_estrutural_quando_todos_os_campos_sao_iguais()
    {
        var a = new DadosFaturado("Joao", "12345678909", "joao@x.com", "11999998888");
        var b = new DadosFaturado("Joao", "12345678909", "joao@x.com", "11999998888");

        a.Should().Be(b);
        a.GetHashCode().Should().Be(b.GetHashCode());
    }

    [Fact]
    public void Equality_diferencia_quando_email_muda()
    {
        var a = new DadosFaturado("Joao", Email: "a@x.com");
        var b = new DadosFaturado("Joao", Email: "b@x.com");

        a.Should().NotBe(b);
    }

    [Fact]
    public void SchemaVersao_pode_ser_sobrescrito()
    {
        var faturado = new DadosFaturado("Teste", SchemaVersao: 2);
        faturado.SchemaVersao.Should().Be(2);
    }
}
