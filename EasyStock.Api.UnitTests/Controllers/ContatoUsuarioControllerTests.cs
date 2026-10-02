using System.Reflection;
using EasyStock.Api.Controllers;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.RateLimiting;

namespace EasyStock.Api.UnitTests.Controllers;

/// <summary>N4: a superfície HTTP da troca de contato: limite <c>auth</c>, login obrigatório e verificação só do SuperAdmin.</summary>
public class ContatoUsuarioControllerTests
{
    private static MethodInfo Acao(Type controller, string nome) =>
        controller.GetMethod(nome, BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)!;

    [Theory]
    [InlineData(typeof(AuthController), "UpdateMe")]
    [InlineData(typeof(ContatoUsuarioController), "DefinirTelefone")]
    public void TrocaDeContatoTemOLimiteAuth(Type controller, string acao)
    {
        // O endpoint exige a senha atual e vira oraculo de senha: vale o limite "auth" (20 por minuto por IP).
        Acao(controller, acao).GetCustomAttributes<EnableRateLimitingAttribute>()
            .Should().ContainSingle().Which.PolicyName.Should().Be("auth");
    }

    [Fact]
    public void PatchMeContinuaAutenticado()
    {
        var acao = Acao(typeof(AuthController), "UpdateMe");

        acao.GetCustomAttributes<AuthorizeAttribute>().Should().NotBeEmpty();
        acao.GetCustomAttributes().OfType<IRouteTemplateProvider>().Single().Template.Should().Be("me");
    }

    [Fact]
    public void TelefoneDoProprioUsuarioExigeLoginENaoEAnonimo()
    {
        var acao = Acao(typeof(ContatoUsuarioController), "DefinirTelefone");

        acao.GetCustomAttributes<AuthorizeAttribute>().Should().ContainSingle().Which.Policy.Should().BeNull();
        acao.GetCustomAttributes<AllowAnonymousAttribute>().Should().BeEmpty();
        acao.GetCustomAttributes().OfType<IRouteTemplateProvider>().Single().Template.Should().Be("auth/me/telefone");
    }

    [Fact]
    public void VerificacaoDoTelefoneESoDoSuperAdmin()
    {
        var acao = Acao(typeof(ContatoUsuarioController), "VerificarTelefone");

        acao.GetCustomAttributes<AuthorizeAttribute>().Should().ContainSingle().Which.Policy.Should().Be("SuperAdmin");
        acao.GetCustomAttributes().OfType<IRouteTemplateProvider>().Single().Template
            .Should().Be("admin/usuarios/{id:guid}/telefone/verificar");
    }
}
