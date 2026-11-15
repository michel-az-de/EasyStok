using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.Domain.Exceptions;
using FluentAssertions;

namespace EasyStock.Domain.Tests.Entities.Atendimento;

/// <summary>#2658 (homologação #1769, E1): o telefone do entregador segue a régua do <c>Telefone</c> (7 a 15 dígitos).</summary>
public class EntregadorTelefoneTests
{
    private static readonly Guid Empresa = Guid.NewGuid();
    private static readonly DateTime Agora = new(2026, 11, 15, 10, 0, 0, DateTimeKind.Utc);

    private static Entregador Novo(string? telefone) => Entregador.Criar(
        Empresa, "José", TipoEntregador.Motoboy, EmpresaEntregador.Propria, telefone, "Moto", null, Agora);

    [Theory]
    [InlineData("123")]
    [InlineData("abc")]
    [InlineData("1199999000011112222")]
    public void CriarComTelefoneInvalidoRecusa(string telefone)
    {
        var act = () => Novo(telefone);

        act.Should().Throw<RegraDeDominioVioladaException>().WithMessage("*Telefone*");
    }

    [Fact]
    public void AtualizarComTelefoneInvalidoRecusaEMantemOAnterior()
    {
        var entregador = Novo("(11) 99999-0000");

        var act = () => entregador.Atualizar("José", TipoEntregador.Motoboy, EmpresaEntregador.Propria, "123", "Moto", null, Agora);

        act.Should().Throw<RegraDeDominioVioladaException>().WithMessage("*Telefone*");
        entregador.Telefone.Should().Be("(11) 99999-0000");
    }

    [Theory]
    [InlineData("(11) 99999-0000")]
    [InlineData("+55 11 99999-0000")]
    [InlineData("11999990000")]
    public void TelefoneValidoGravaComoDigitado(string telefone)
    {
        Novo(telefone).Telefone.Should().Be(telefone);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TelefoneVazioContinuaOpcional(string? telefone)
    {
        Novo(telefone).Telefone.Should().BeNull();
    }
}
