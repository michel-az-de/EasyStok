using EasyStock.Domain.Entities;
using FluentAssertions;

namespace EasyStock.Domain.Tests.Entities.Clientes;

/// <summary>S25: chave do sinal "mesmo domicílio" com a normalização de FreteZona.</summary>
public class ClienteEnderecoTests
{
    [Fact]
    public void ChaveDomicilioIgnoraMascaraCaixaEAcento()
    {
        var a = ClienteEndereco.ChaveDomicilio("05500-000", " 12 ", "Apto Ç 3");
        var b = ClienteEndereco.ChaveDomicilio("05500000", "12", "apto c 3");

        a.Should().Be("05500000|12|apto c 3");
        b.Should().Be(a);
    }

    [Theory]
    [InlineData(null, "12")]
    [InlineData("0550", "12")]
    [InlineData("05500-000", null)]
    [InlineData("05500-000", "  ")]
    public void EnderecoIncompletoNaoTemChave(string? cep, string? numero) =>
        ClienteEndereco.ChaveDomicilio(cep, numero, null).Should().BeNull();
}
