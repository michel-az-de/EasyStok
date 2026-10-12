using EasyStock.Domain.Entities.Storefront;
using EasyStock.Domain.Enums.Storefront;
using EasyStock.Domain.Exceptions;
using FluentAssertions;

namespace EasyStock.Domain.Tests.Entities.Storefront;

/// <summary>Avaliação de um toque (S26): positiva ou negativa, sem estrelas.</summary>
public class PedidoAvaliacaoSimplesTests
{
    private static readonly DateTime Solicitado = new(2026, 9, 30, 13, 30, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(ResultadoAvaliacao.Positiva)]
    [InlineData(ResultadoAvaliacao.Negativa)]
    public void CriarSimplesGuardaResultadoSemEstrelas(ResultadoAvaliacao resultado)
    {
        var avaliacao = PedidoAvaliacao.CriarSimples(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), resultado, "  veio frio  ", Solicitado);

        avaliacao.Resultado.Should().Be(resultado);
        avaliacao.Estrelas.Should().BeNull();
        avaliacao.Comentario.Should().Be("veio frio");
        avaliacao.SolicitadoEm.Should().Be(Solicitado);
        avaliacao.RespondidoEm.Should().NotBeNull();
    }

    [Fact]
    public void CriarSimplesExigePedido()
    {
        var act = () => PedidoAvaliacao.CriarSimples(Guid.Empty, Guid.NewGuid(), Guid.NewGuid(), ResultadoAvaliacao.Positiva, null, Solicitado);

        act.Should().Throw<RegraDeDominioVioladaException>();
    }

    [Fact]
    public void CriarComEstrelasNaoTemResultado()
    {
        var avaliacao = PedidoAvaliacao.Criar(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 4, null, true, null, Solicitado);

        avaliacao.Resultado.Should().BeNull();
        avaliacao.Estrelas.Should().Be(4);
    }
}
