using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Exceptions;
using FluentAssertions;

namespace EasyStock.Domain.Tests.Entities.Atendimento;

/// <summary>#2937: a frase de fecho do canhoto é da loja; nulo mantém, vazio tira.</summary>
public class FraseFechoCanhotoTests
{
    [Fact]
    public void LojaNova_NaoTemFecho() =>
        ConfiguracaoAtendimento.CriarPadrao(Guid.NewGuid()).FraseFechoCanhoto.Should().BeNull();

    [Fact]
    public void Definir_GuardaAparado_NuloMantem_VazioTira()
    {
        var c = ConfiguracaoAtendimento.CriarPadrao(Guid.NewGuid());

        c.DefinirFraseFechoCanhoto("  Boa massa!  ");
        c.FraseFechoCanhoto.Should().Be("Boa massa!");

        c.DefinirFraseFechoCanhoto(null);
        c.FraseFechoCanhoto.Should().Be("Boa massa!");

        c.DefinirFraseFechoCanhoto("   ");
        c.FraseFechoCanhoto.Should().BeNull();
    }

    [Theory]
    [InlineData("linha um\nlinha dois")]
    [InlineData("0123456789012345678901234567890123456789012345678901234567890")]
    public void Definir_RecusaLongaOuComQuebra(string frase)
    {
        var c = ConfiguracaoAtendimento.CriarPadrao(Guid.NewGuid());

        var acao = () => c.DefinirFraseFechoCanhoto(frase);

        acao.Should().Throw<RegraDeDominioVioladaException>();
    }
}
