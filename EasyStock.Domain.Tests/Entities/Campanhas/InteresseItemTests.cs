using EasyStock.Domain.Entities.Campanhas;
using EasyStock.Domain.Exceptions;
using FluentAssertions;

namespace EasyStock.Domain.Tests.Entities.Campanhas;

/// <summary>
/// Interesse do cliente num item indisponível (S31, US-057): com o item do cardápio quando ele foi
/// identificado, só com a descrição livre quando não; aberto até ser atendido.
/// </summary>
public class InteresseItemTests
{
    private static readonly Guid Empresa = Guid.NewGuid();
    private static readonly Guid Cliente = Guid.NewGuid();
    private static readonly DateTime Agora = new(2026, 9, 30, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void CriaComOuSemItem()
    {
        var itemId = Guid.NewGuid();

        var comItem = InteresseItem.Registrar(Empresa, Cliente, itemId, null, OrigemInteresse.Agente, Agora);
        comItem.CardapioItemId.Should().Be(itemId);
        comItem.Descricao.Should().BeNull();
        comItem.Origem.Should().Be("agente");
        comItem.RegistradoEm.Should().Be(Agora);
        comItem.Aberto.Should().BeTrue();

        var semItem = InteresseItem.Registrar(Empresa, Cliente, null, "  Bolo de fubá  ", OrigemInteresse.Dona, Agora);
        semItem.CardapioItemId.Should().BeNull();
        semItem.Descricao.Should().Be("Bolo de fubá");
        semItem.Origem.Should().Be("dona");
    }

    [Fact]
    public void SemItemESemDescricao_Recusa()
    {
        var act = () => InteresseItem.Registrar(Empresa, Cliente, null, "   ", OrigemInteresse.Agente, Agora);
        act.Should().Throw<RegraDeDominioVioladaException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("campanha")]
    public void OrigemForaDaLista_Recusa(string origem)
    {
        var act = () => InteresseItem.Registrar(Empresa, Cliente, Guid.NewGuid(), null, origem, Agora);
        act.Should().Throw<RegraDeDominioVioladaException>();
    }

    [Fact]
    public void MarcarAtendido_FechaUmaVez()
    {
        var interesse = InteresseItem.Registrar(Empresa, Cliente, Guid.NewGuid(), null, OrigemInteresse.Agente, Agora);

        interesse.MarcarAtendido(Agora.AddDays(2));
        interesse.MarcarAtendido(Agora.AddDays(3));

        interesse.Aberto.Should().BeFalse();
        interesse.AtendidoEm.Should().Be(Agora.AddDays(2));
    }
}
