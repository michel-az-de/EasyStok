using EasyStock.Domain.Entities;
using EasyStock.Domain.Exceptions;
using FluentAssertions;

namespace EasyStock.Domain.Tests.Entities.Clientes;

/// <summary>S24: nota interna datada, até 500 caracteres, opcionalmente presa a pedido ou mensagem.</summary>
public class ClienteNotaTests
{
    private static readonly DateTime Agora = new(2026, 9, 30, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void CriarGuardaTextoAutorEVinculos()
    {
        var empresaId = Guid.NewGuid();
        var clienteId = Guid.NewGuid();
        var pedidoId = Guid.NewGuid();

        var nota = ClienteNota.Criar(empresaId, clienteId, "  prefere entrega depois das 18h ", "Baba", Agora, pedidoId);

        nota.EmpresaId.Should().Be(empresaId);
        nota.ClienteId.Should().Be(clienteId);
        nota.Texto.Should().Be("prefere entrega depois das 18h");
        nota.Autor.Should().Be("Baba");
        nota.PedidoId.Should().Be(pedidoId);
        nota.MensagemId.Should().BeNull();
        nota.CriadoEm.Should().Be(Agora);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void TextoVazioEhRecusado(string texto)
    {
        var act = () => ClienteNota.Criar(Guid.NewGuid(), Guid.NewGuid(), texto, "Baba", Agora);
        act.Should().Throw<RegraDeDominioVioladaException>();
    }

    [Fact]
    public void TextoAcimaDe500EhRecusado()
    {
        var act = () => ClienteNota.Criar(Guid.NewGuid(), Guid.NewGuid(), new string('x', 501), "Baba", Agora);
        act.Should().Throw<RegraDeDominioVioladaException>();
    }

    [Fact]
    public void AutorVazioEhRecusado()
    {
        var act = () => ClienteNota.Criar(Guid.NewGuid(), Guid.NewGuid(), "texto", " ", Agora);
        act.Should().Throw<RegraDeDominioVioladaException>();
    }
}
