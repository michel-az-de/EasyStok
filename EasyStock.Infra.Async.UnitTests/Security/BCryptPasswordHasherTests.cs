using EasyStock.Domain.Entities;
using FluentAssertions;

namespace EasyStock.Infra.Async.UnitTests.Security;

/// <summary>
/// N9: prova com o BCrypt de verdade que o hash do convidado nunca confere e nunca lança. Se o marcador passar a lançar
/// <c>SaltParseException</c> sem o hasher capturar, o login do convidado viraria erro 500 em vez de senha errada.
/// </summary>
public class BCryptPasswordHasherTests
{
    private readonly BCryptPasswordHasher _hasher = new();

    [Theory]
    [InlineData("Senha@12345")]
    [InlineData("")]
    [InlineData("$2a$10$CONVIDADO_")]
    [InlineData("qualquer coisa")]
    public void HashDoConvidadoNuncaConfere(string tentativa)
    {
        var convidado = Usuario.CriarConvidado("Ana", "ana@casadababa.com");

        var act = () => _hasher.Verify(tentativa, convidado.SenhaHash);

        act.Should().NotThrow();
        _hasher.Verify(tentativa, convidado.SenhaHash).Should().BeFalse();
    }

    [Fact]
    public void HashDeQuemAceitouPeloGoogleNuncaConfere()
    {
        var usuario = Usuario.CriarConvidado("Ana", "ana@casadababa.com");
        usuario.AceitarConviteSemSenha(ViaDoConvite.Google, DateTime.UtcNow);

        _hasher.Verify("Senha@12345", usuario.SenhaHash).Should().BeFalse();
    }

    [Fact]
    public void SenhaDefinidaNoAceiteConfere()
    {
        var convidado = Usuario.CriarConvidado("Ana", "ana@casadababa.com");

        convidado.AceitarConvite(_hasher.Hash("Nova@Senha123"), ViaDoConvite.Email, DateTime.UtcNow);

        _hasher.Verify("Nova@Senha123", convidado.SenhaHash).Should().BeTrue();
    }
}
