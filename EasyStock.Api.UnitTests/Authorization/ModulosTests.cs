using System.Reflection;
using System.Security.Claims;
using EasyStock.Api.Authorization;
using EasyStock.Api.Services;
using EasyStock.Domain.Enums;
using EasyStock.Domain.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.AspNetCore.Mvc.Authorization;

namespace EasyStock.Api.UnitTests.Authorization;

public class ModulosTests
{
    [Theory]
    [InlineData("RegistrarEstornoManual", "Gerente")]
    [InlineData("EstornosManuais", "Operador")]
    [InlineData("EstornosOnline", "Operador")]
    [InlineData("SolicitarEstornoOnline", "Gerente")]
    [InlineData("RetomarEstornoOnline", "Gerente")]
    public void Devolucao_exige_nivel_e_modulo_no_servidor(string acao, string nivel)
    {
        var tipo = typeof(Api.Controllers.PedidosCobrancaController);
        tipo.GetMethod(acao)!.GetCustomAttributes<AuthorizeAttribute>().Should().Contain(a => a.Policy == nivel);
        ModulosConvention.ModulosDe(tipo, acao)!.Should().Equal(Modulo.Atendimento);
    }

    [Fact]
    public void TodoControllerConcreto_PrecisaDeClassificacaoExplicita()
    {
        var controllers = typeof(Api.Controllers.AuthController).Assembly.GetTypes()
            .Where(t => !t.IsAbstract && t.IsSubclassOf(typeof(ControllerBase))).ToArray();
        controllers.Should().NotBeEmpty();
        controllers.Where(t => !ModulosConvention.Classificado(t)).Select(t => t.FullName).Should().BeEmpty();
    }

    [Fact]
    public void Troca_de_janela_exige_modulo_atendimento_ou_entregas() =>
        ModulosConvention.ModulosDe(typeof(Api.Controllers.PedidoJanelaController), "Trocar")!
            .Should().Equal(Modulo.Atendimento, Modulo.Entregas);

    [Fact]
    public void ConvencaoInstalaFiltroRealSemSubstituirOutrosFiltros()
    {
        var app = new ApplicationModel();
        var controller = new ControllerModel(typeof(Api.Controllers.CaixaController).GetTypeInfo(), []);
        var metodo = typeof(Api.Controllers.CaixaController).GetMethods().First(m => m.DeclaringType == typeof(Api.Controllers.CaixaController));
        var action = new ActionModel(metodo, []) { Controller = controller };
        var original = new AuthorizeFilter("Operador");
        action.Filters.Add(original);
        controller.Actions.Add(action);
        app.Controllers.Add(controller);
        new ModulosConvention().Apply(app);
        action.Filters.Should().Contain(original);
        action.Filters.OfType<AuthorizeFilter>().Single(f => f.Policy != null).Policy!.Requirements
            .OfType<ModuloRequirement>().Single().Modulos.Should().Equal(Modulo.Caixa);
    }

    [Fact]
    public void CatalogoCompartilhado_NaoLiberaPedidoDaConversa()
    {
        ModulosConvention.ModulosDe(typeof(Api.Controllers.AtendimentoComandaController), "Cardapio")!
            .Should().Contain(Modulo.Producao);
        ModulosConvention.ModulosDe(typeof(Api.Controllers.AtendimentoComandaController), "Pedido")!
            .Should().Equal(Modulo.Atendimento);
    }

    [Theory]
    [InlineData("Dona", Modulo.Caixa, true)]
    [InlineData("Atendimento", Modulo.Caixa, true)]
    [InlineData("Atendimento", Modulo.Configuracoes, false)]
    [InlineData("Cozinha", Modulo.Caixa, false)]
    [InlineData("Cozinha", Modulo.Cozinha, true)]
    [InlineData("Cozinha", Modulo.Producao, true)]
    [InlineData("Cozinha", Modulo.Atendimento, false)]
    public async Task HandlerUsaClaimsReais(string nome, Modulo modulo, bool esperado)
    {
        var perfil = PerfisCasaDaBaba.Iniciais.Single(p => p.Nome == nome);
        var claims = new List<Claim> { new("nivel", perfil.Nivel.ToString()), new("empresaId", Guid.NewGuid().ToString()) };
        claims.AddRange(perfil.Permissoes.Select(p => new Claim("permissao", p.ToString())));
        (await Autorizar(claims, new ModuloRequirement(modulo))).Should().Be(esperado);
    }

    [Fact]
    public async Task SemEmpresa_NegaMesmoParaAdmin() =>
        (await Autorizar([new("nivel", "Admin")], new ModuloRequirement(Modulo.Caixa))).Should().BeFalse();

    [Fact]
    public async Task BridgeSoPassaEmRequisitoDeImpressao()
    {
        Claim[] claims = [new("empresaId", Guid.NewGuid().ToString())];
        (await Autorizar(claims, new ModuloRequirement(Modulo.Cozinha) { AceitaBridge = true }, ImpressaoApiKeyAuthHandler.SchemeName)).Should().BeTrue();
        (await Autorizar(claims, new ModuloRequirement(Modulo.Caixa), ImpressaoApiKeyAuthHandler.SchemeName)).Should().BeFalse();
    }

    private static async Task<bool> Autorizar(IEnumerable<Claim> claims, ModuloRequirement requirement, string scheme = "test")
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, scheme));
        var usuario = new CurrentUserAccessor(new HttpContextAccessor { HttpContext = new DefaultHttpContext { User = principal } });
        var context = new AuthorizationHandlerContext([requirement], principal, null);
        await new ModuloAuthorizationHandler(usuario).HandleAsync(context);
        return context.HasSucceeded;
    }
}
