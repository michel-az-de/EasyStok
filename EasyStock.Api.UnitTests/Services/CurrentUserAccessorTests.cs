using EasyStock.Api.Services;
using EasyStock.Domain.Enums;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;

namespace EasyStock.Api.UnitTests.Services;

public class CurrentUserAccessorTests
{
    [Theory]
    [InlineData("permissoesExplicitas", "true")]
    [InlineData("permissao", "ConfigurarSla")]
    [InlineData("permissao", "15")]
    public void ListaVaziaOuClaimRemovida_NaoLiberaFallback(string tipo, string valor)
    {
        var accessor = CreateAccessor(true, new Claim("nivel", "Admin"), new Claim(tipo, valor));
        foreach (var permissao in Enum.GetValues<Permissao>().Except(EasyStock.Domain.Services.PermissoesLegadas.Valores))
            accessor.TemPermissao(permissao).Should().BeFalse();
    }

    [Fact]
    public void ClaimRemovidaMisturadaComAtual_PreservaSomenteAPermissaoAtual()
    {
        var accessor = CreateAccessor(true, new Claim("nivel", "Admin"),
            new Claim("permissao", "ConfigurarSla"), new Claim("permissao", "AtenderConversas"));
        accessor.TemPermissao(Permissao.AtenderConversas).Should().BeTrue();
        accessor.TemPermissao(Permissao.AcessarModuloConfiguracoes).Should().BeFalse();
    }

    [Fact]
    public void EmpresaId_DeveRetornarGuidEmpty_QuandoClaimAusente()
    {
        var accessor = CreateAccessor(isAuthenticated: true);

        accessor.EmpresaId.Should().Be(Guid.Empty);
    }

    [Fact]
    public void UsuarioId_DeveRetornarGuidEmpty_QuandoClaimInvalida()
    {
        var accessor = CreateAccessor(
            isAuthenticated: true,
            new Claim("sub", "nao-e-guid"));

        accessor.UsuarioId.Should().Be(Guid.Empty);
    }

    [Fact]
    public void Nivel_DeveRetornarVisualizador_QuandoClaimInvalida()
    {
        var accessor = CreateAccessor(
            isAuthenticated: true,
            new Claim("nivel", "nao-existe"));

        accessor.Nivel.Should().Be(NivelAcesso.Visualizador);
    }

    [Fact]
    public void TemPermissao_DeveRetornarFalse_QuandoNaoAutenticado()
    {
        var accessor = CreateAccessor(
            isAuthenticated: false,
            new Claim("nivel", NivelAcesso.Admin.ToString()));

        accessor.TemPermissao(Permissao.GerenciarUsuarios).Should().BeFalse();
    }

    [Fact]
    public void TemPermissao_DeveUsarClaimsExplicitas_QuandoExistirem()
    {
        var accessor = CreateAccessor(
            isAuthenticated: true,
            new Claim("nivel", NivelAcesso.Admin.ToString()),
            new Claim("permissao", Permissao.VisualizarRelatorios.ToString()));

        accessor.TemPermissao(Permissao.VisualizarRelatorios).Should().BeTrue();
        accessor.TemPermissao(Permissao.GerenciarUsuarios).Should().BeFalse();
    }

    [Fact]
    public void TemPermissao_ClaimExplicitaInvalida_NaoPodeLiberarFallback()
    {
        var accessor = CreateAccessor(
            isAuthenticated: true,
            new Claim("nivel", NivelAcesso.Gerente.ToString()),
            new Claim("permissao", "permissao-invalida"));

        accessor.TemPermissao(Permissao.GerenciarProdutos).Should().BeFalse();
        accessor.TemPermissao(Permissao.GerenciarUsuarios).Should().BeFalse();
    }

    [Fact]
    public void TemPermissao_DeveAplicarFallbackDoNivelVisualizador_QuandoClaimNivelAusente()
    {
        var accessor = CreateAccessor(isAuthenticated: true);

        accessor.TemPermissao(Permissao.VisualizarRelatorios).Should().BeTrue();
        accessor.TemPermissao(Permissao.GerenciarProdutos).Should().BeFalse();
    }

    private static CurrentUserAccessor CreateAccessor(bool isAuthenticated, params Claim[] claims)
    {
        var identity = new ClaimsIdentity(claims, isAuthenticated ? "test-auth" : null);
        var principal = new ClaimsPrincipal(identity);
        var httpContext = new DefaultHttpContext { User = principal };
        var httpContextAccessor = new HttpContextAccessor { HttpContext = httpContext };

        return new CurrentUserAccessor(httpContextAccessor);
    }
}
