using EasyStock.Domain.ValueObjects;
using FluentAssertions;

namespace EasyStock.Domain.Tests.ValueObjects;

public class DadosEmissorTests
{
    [Fact]
    public void Construtor_minimo_aceita_apenas_nome()
    {
        var emissor = new DadosEmissor(Nome: "EasyStok LTDA");

        emissor.Nome.Should().Be("EasyStok LTDA");
        emissor.Documento.Should().BeNull();
        emissor.RazaoSocial.Should().BeNull();
        emissor.InscricaoMunicipal.Should().BeNull();
        emissor.InscricaoEstadual.Should().BeNull();
        emissor.RegimeTributario.Should().BeNull();
        emissor.Endereco.Should().BeNull();
        emissor.Email.Should().BeNull();
        emissor.Telefone.Should().BeNull();
        emissor.SchemaVersao.Should().Be(DadosEmissor.SchemaVersaoAtual);
    }

    [Fact]
    public void SchemaVersaoAtual_e_um()
    {
        DadosEmissor.SchemaVersaoAtual.Should().Be(1);
    }

    [Fact]
    public void Construtor_preserva_endereco_aninhado()
    {
        var endereco = new Endereco("Rua X", "10", Bairro: "Centro", Cidade: "SP", Uf: "SP");
        var emissor = new DadosEmissor(
            Nome: "Teste",
            Documento: "11222333000181",
            Endereco: endereco);

        emissor.Endereco.Should().Be(endereco);
    }

    [Fact]
    public void Equality_e_estrutural_incluindo_endereco_aninhado()
    {
        var endereco = new Endereco("Rua X", "10");
        var a = new DadosEmissor("Empresa", "11222333000181", Endereco: endereco);
        var b = new DadosEmissor("Empresa", "11222333000181", Endereco: new Endereco("Rua X", "10"));

        a.Should().Be(b);
        a.GetHashCode().Should().Be(b.GetHashCode());
    }

    [Fact]
    public void Equality_diferencia_quando_documento_muda()
    {
        var a = new DadosEmissor("Empresa", "11222333000181");
        var b = new DadosEmissor("Empresa", "99888777000166");

        a.Should().NotBe(b);
    }

    [Fact]
    public void SchemaVersao_pode_ser_sobrescrito_para_compatibilidade_futura()
    {
        var emissor = new DadosEmissor("Teste", SchemaVersao: 2);
        emissor.SchemaVersao.Should().Be(2);
    }
}
