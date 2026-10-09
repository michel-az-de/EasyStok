using System.Security.Claims;
using EasyStock.Api.Authentication;
using FluentAssertions;

namespace EasyStock.Api.UnitTests.Authentication;

/// <summary>#1474 B1: nome legível do usuário logado vem da claim "nome", depois "email"; nunca do "sub".</summary>
public class ClaimsPrincipalExtensionsTests
{
    private static ClaimsPrincipal Usuario(params Claim[] claims) =>
        new(new ClaimsIdentity(claims.Prepend(new Claim("sub", Guid.NewGuid().ToString())), "Bearer", "sub", "nivel"));

    [Fact]
    public void UsaAClaimNome() =>
        Usuario(new Claim("nome", "Thati"), new Claim("email", "thati@casa.com")).NomeExibicao().Should().Be("Thati");

    [Fact]
    public void SemNomeUsaOEmail() =>
        Usuario(new Claim("email", "thati@casa.com")).NomeExibicao().Should().Be("thati@casa.com");

    [Fact]
    public void SemNomeNemEmailDevolveNulo() =>
        Usuario(new Claim("nome", "  ")).NomeExibicao().Should().BeNull();
}
