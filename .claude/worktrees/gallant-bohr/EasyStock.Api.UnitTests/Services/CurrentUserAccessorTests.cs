using EasyStock.Api.Services;
using EasyStock.Domain.Enums;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;

namespace EasyStock.Api.UnitTests.Services;

public class CurrentUserAccessorTests
{
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
    public void TemPermissao_DeveIgnorarClaimInvalida_EAplicarFallbackPorNivel()
    {
        var accessor = CreateAccessor(
            isAuthenticated: true,
            new Claim("nivel", NivelAcesso.Gerente.ToString()),
            new Claim("permissao", "permissao-invalida"));

        accessor.TemPermissao(Permissao.GerenciarProdutos).Should().BeTrue();
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
