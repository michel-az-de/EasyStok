using EasyStock.Api.Observability;
using FluentAssertions;

namespace EasyStock.Api.UnitTests.Observability;

public class CorrelationIdHeaderTests
{
    [Theory]
    [InlineData("3f2b8c1e-6a4d-4f0b-9d57-1c2e3a4b5c6d")]
    [InlineData("00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01")]
    [InlineData("pedido_123.retry")]
    public void Identificador_do_cliente_e_preservado(string recebido)
    {
        CorrelationIdHeader.Resolver(recebido).Should().Be(recebido);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("'><img src=x onerror=alert(1)>")]
    [InlineData("\";alert(1)//")]
    [InlineData("abc def")]
    [InlineData("linha\r\nforjada")]
    [InlineData("çedilha")]
    public void Valor_que_nao_e_identificador_vira_id_gerado(string? recebido)
    {
        var resolvido = CorrelationIdHeader.Resolver(recebido);

        resolvido.Should().NotBe(recebido);
        Guid.TryParse(resolvido, out _).Should().BeTrue();
    }

    [Fact]
    public void Valor_acima_do_tamanho_maximo_vira_id_gerado()
    {
        var noLimite = new string('a', CorrelationIdHeader.TamanhoMaximo);

        CorrelationIdHeader.Resolver(noLimite).Should().Be(noLimite);
        Guid.TryParse(CorrelationIdHeader.Resolver(noLimite + "a"), out _).Should().BeTrue();
    }
}
