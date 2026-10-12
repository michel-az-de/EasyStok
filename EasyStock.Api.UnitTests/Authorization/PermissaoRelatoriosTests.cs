using System.Reflection;
using System.Security.Claims;
using EasyStock.Api.Authorization;
using EasyStock.Api.Configuration;
using EasyStock.Api.Controllers;
using EasyStock.Api.Services;
using EasyStock.Domain.Enums;
using EasyStock.Domain.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EasyStock.Api.UnitTests.Authorization;

/// <summary>
/// #1508: analytics financeiro, inteligência e auditoria de entidade ficavam só com [Authorize];
/// Permissao.VisualizarRelatorios não era aplicada em lugar nenhum.
/// </summary>
public class PermissaoRelatoriosTests
{
    [Theory]
    [InlineData(nameof(AnalyticsController.Receita))]
    [InlineData(nameof(AnalyticsController.Margem))]
    [InlineData(nameof(AnalyticsController.ReceitaCusto))]
    public void AnalyticsFinanceiro_ExigeVisualizarRelatorios(string acao) =>
        Policies(typeof(AnalyticsController).GetMethod(acao)!).Should().Contain(PoliticasPermissao.VisualizarRelatorios);

    [Theory]
    [InlineData(typeof(InteligenciaController))]
    [InlineData(typeof(InteligenciaLojasController))]
    public void Inteligencia_ExigeVisualizarRelatorios(Type controller) =>
        Policies(controller).Should().Contain(PoliticasPermissao.VisualizarRelatorios);

    [Fact]
    public void AuditoriaDeEntidade_ExigeAdmin() =>
        Policies(typeof(EntityAuditController)).Should().Contain("Admin");

    [Theory]
    [InlineData("Dona", true)]
    [InlineData("Atendimento", false)]
    [InlineData("Cozinha", false)]
    public async Task PerfisCasaDaBaba_SoADonaVeRelatorios(string nome, bool esperado)
    {
        var perfil = PerfisCasaDaBaba.Iniciais.Single(p => p.Nome == nome);
        var claims = new List<Claim> { new("nivel", perfil.Nivel.ToString()), new("empresaId", Guid.NewGuid().ToString()) };
        claims.AddRange(perfil.Permissoes.Select(p => new Claim("permissao", p.ToString())));

        (await Autorizar(claims)).Should().Be(esperado);
    }

    [Theory]
    [InlineData(NivelAcesso.Admin, true)]
    [InlineData(NivelAcesso.Gerente, true)]
    [InlineData(NivelAcesso.Operador, false)]
    public async Task SemPerfilExplicito_UsaFallbackDoNivel(NivelAcesso nivel, bool esperado) =>
        (await Autorizar([new("nivel", nivel.ToString()), new("empresaId", Guid.NewGuid().ToString())]))
            .Should().Be(esperado);

    [Fact]
    public async Task PolicyEHandler_EstaoRegistradosNoContainer()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:SecretKey"] = "chave-de-teste-com-mais-de-32-caracteres-ok",
            ["Jwt:Issuer"] = "teste",
            ["Jwt:Audience"] = "teste",
        }).Build();
        var services = new ServiceCollection().AddLogging();
        services.AddEasyStockAuth(config);
        await using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var policy = await scope.ServiceProvider.GetRequiredService<IAuthorizationPolicyProvider>()
            .GetPolicyAsync(PoliticasPermissao.VisualizarRelatorios);
        policy!.Requirements.OfType<PermissaoRequirement>().Single().Permissao.Should().Be(Permissao.VisualizarRelatorios);
        scope.ServiceProvider.GetServices<IAuthorizationHandler>().Should().Contain(h => h is PermissaoAuthorizationHandler);
    }

    private static string?[] Policies(MemberInfo membro) =>
        membro.GetCustomAttributes<AuthorizeAttribute>(inherit: true).Select(a => a.Policy).ToArray();

    private static async Task<bool> Autorizar(IEnumerable<Claim> claims)
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
        var usuario = new CurrentUserAccessor(new HttpContextAccessor { HttpContext = new DefaultHttpContext { User = principal } });
        var requirement = new PermissaoRequirement(Permissao.VisualizarRelatorios);
        var context = new AuthorizationHandlerContext([requirement], principal, null);
        await new PermissaoAuthorizationHandler(usuario).HandleAsync(context);
        return context.HasSucceeded;
    }
}
