using EasyStock.Domain.Entities;
using EasyStock.Domain.Exceptions;
using FluentAssertions;

namespace EasyStock.Domain.Tests.Entities;

/// <summary>#2070: a marca de um passo só existe para id do catálogo, empresa e autor informados.</summary>
public class PassoConfiguracaoInicialTests
{
    private static readonly DateTime Agora = new(2026, 10, 26, 13, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void MarcarGuardaPassoSituacaoInstanteEAutor()
    {
        var empresaId = Guid.NewGuid();
        var usuarioId = Guid.NewGuid();

        var passo = PassoConfiguracaoInicial.Marcar(empresaId, "frete", SituacaoDoPasso.Depois, usuarioId, Agora);

        passo.EmpresaId.Should().Be(empresaId);
        passo.Passo.Should().Be("frete");
        passo.Situacao.Should().Be(SituacaoDoPasso.Depois);
        passo.Em.Should().Be(Agora);
        passo.PorUsuarioId.Should().Be(usuarioId);
    }

    [Theory]
    [InlineData("caixa")]
    [InlineData("")]
    [InlineData("Frete")]
    public void PassoForaDoCatalogoEhRecusado(string passo)
    {
        var marcar = () => PassoConfiguracaoInicial.Marcar(Guid.NewGuid(), passo, SituacaoDoPasso.Conferido, Guid.NewGuid(), Agora);

        marcar.Should().Throw<RegraDeDominioVioladaException>().WithMessage("Passo desconhecido*");
    }

    [Fact]
    public void EmpresaVaziaEhRecusada() =>
        FluentActions.Invoking(() => PassoConfiguracaoInicial.Marcar(Guid.Empty, "frete", SituacaoDoPasso.Conferido, Guid.NewGuid(), Agora))
            .Should().Throw<RegraDeDominioVioladaException>().WithMessage("EmpresaId é obrigatório.");

    [Fact]
    public void AutorVazioEhRecusado() =>
        FluentActions.Invoking(() => PassoConfiguracaoInicial.Marcar(Guid.NewGuid(), "frete", SituacaoDoPasso.Conferido, Guid.Empty, Agora))
            .Should().Throw<RegraDeDominioVioladaException>().WithMessage("Informe quem marcou o passo.");

    [Fact]
    public void SituacaoForaDoEnumEhRecusada() =>
        FluentActions.Invoking(() => PassoConfiguracaoInicial.Marcar(Guid.NewGuid(), "frete", (SituacaoDoPasso)9, Guid.NewGuid(), Agora))
            .Should().Throw<RegraDeDominioVioladaException>().WithMessage("Situação do passo inválida.");

    [Theory]
    [InlineData(SituacaoDoPasso.Conferido, "conferido")]
    [InlineData(SituacaoDoPasso.Depois, "depois")]
    public void SituacaoTemOTextoDoContratoNosDoisSentidos(SituacaoDoPasso situacao, string texto)
    {
        SituacoesDoPasso.Texto(situacao).Should().Be(texto);
        SituacoesDoPasso.Ler(texto).Should().Be(situacao);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Conferido")]
    [InlineData("pulado")]
    public void TextoForaDoContratoNaoViraSituacao(string? texto) =>
        SituacoesDoPasso.Ler(texto).Should().BeNull();
}
