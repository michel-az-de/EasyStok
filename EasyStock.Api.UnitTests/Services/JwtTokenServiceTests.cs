using System.IdentityModel.Tokens.Jwt;
using EasyStock.Api.Services;
using EasyStock.Application.UseCases.AutenticarUsuario;
using EasyStock.Domain.Enums;
using FluentAssertions;
using Microsoft.Extensions.Configuration;

namespace EasyStock.Api.UnitTests.Services;

/// <summary>
/// #1352 (N7): contrato entre quem emite o JWT e o <c>ValidadorSessaoUsuario</c>, que lê <c>sub</c> (o usuário)
/// e <c>iat</c> (em segundos inteiros) de todo token. Se o emissor deixar de gravar um dos dois, todo token
/// passaria a falhar com 401 em produção.
/// </summary>
public class JwtTokenServiceTests
{
    [Fact]
    public void TokenCarregaSubEIatEmSegundosInteirosParaOValidadorDeSessao()
    {
        var configuracao = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:SecretKey"] = "chave-de-teste-do-jwt-com-mais-de-32-caracteres",
            ["Jwt:Issuer"] = "teste",
            ["Jwt:Audience"] = "teste",
            ["Jwt:ExpirationMinutes"] = "480",
        }).Build();
        var usuarioId = Guid.NewGuid();
        var antes = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var token = new JwtTokenService(configuracao)
            .GerarToken(new AutenticarUsuarioResult(usuarioId, null, "Ana", "ana@casadababa.com", NivelAcesso.Admin, []));
        var depois = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var lido = new JwtSecurityTokenHandler().ReadJwtToken(token);
        lido.Claims.Single(c => c.Type == "sub").Value.Should().Be(usuarioId.ToString());
        long.Parse(lido.Claims.Single(c => c.Type == "iat").Value).Should().BeInRange(antes, depois);
        long.Parse(lido.Claims.Single(c => c.Type == "exp").Value).Should().Be(
            long.Parse(lido.Claims.Single(c => c.Type == "iat").Value) + 480 * 60);
    }
}
