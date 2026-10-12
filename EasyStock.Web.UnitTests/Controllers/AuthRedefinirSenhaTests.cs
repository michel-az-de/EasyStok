using System.Net;
using System.Text;
using EasyStock.Web.Controllers;
using EasyStock.Web.Models.ViewModels.Auth;
using EasyStock.Web.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace EasyStock.Web.UnitTests.Controllers;

/// <summary>
/// N8: o token do link e o código do WhatsApp nunca vazam pelo <c>Referer</c>, a tela do código posta na API e o pedido de
/// redefinição manda só o e-mail (a base do link vem da configuração da API, não do Web).
/// </summary>
public class AuthRedefinirSenhaTests
{
    private sealed class GravaRequisicoes(HttpStatusCode status = HttpStatusCode.OK, string corpo = """{"data":{"success":true},"meta":{}}""")
        : HttpMessageHandler
    {
        public List<(string Caminho, string Corpo)> Pedidos { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Pedidos.Add((request.RequestUri!.AbsolutePath, request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken)));
            return new HttpResponseMessage(status) { Content = new StringContent(corpo, Encoding.UTF8, "application/json") };
        }
    }

    private static (AuthController Controller, GravaRequisicoes Api) Montar(HttpStatusCode status = HttpStatusCode.OK, string? corpo = null)
    {
        var handler = corpo is null ? new GravaRequisicoes(status) : new GravaRequisicoes(status, corpo);
        var api = new ApiClient(new HttpClient(handler) { BaseAddress = new Uri("http://api.test/") }, NullLogger<ApiClient>.Instance);
        var http = new DefaultHttpContext();
        var env = Substitute.For<IWebHostEnvironment>();
        var controller = new AuthController(
            api, new SessionService(Substitute.For<IHttpContextAccessor>()), env, Substitute.For<IJwtClaimsReader>())
        {
            ControllerContext = new ControllerContext { HttpContext = http },
            TempData = new TempDataDictionary(http, Substitute.For<ITempDataProvider>()),
        };
        return (controller, handler);
    }

    [Fact]
    public void RedefinirSenhaEnviaReferrerPolicyNoReferrer()
    {
        var (controller, _) = Montar();

        controller.RedefinirSenha("token-do-link");

        controller.Response.Headers["Referrer-Policy"].ToString().Should().Be("no-referrer");
    }

    [Fact]
    public void TelaDoCodigoTambemEnviaReferrerPolicyNoReferrer()
    {
        var (controller, _) = Montar();

        controller.RedefinirSenhaCodigo();

        controller.Response.Headers["Referrer-Policy"].ToString().Should().Be("no-referrer");
    }

    [Fact]
    public async Task TelaDoCodigoPostaNaApi()
    {
        var (controller, api) = Montar();

        var resultado = await controller.RedefinirSenhaCodigo(new ResetPasswordCodeViewModel
        {
            Email = "ana@casadababa.com", Codigo = "123456", NovaSenha = "Nova@Senha123", ConfirmarSenha = "Nova@Senha123",
        });

        resultado.Should().BeOfType<RedirectToActionResult>().Which.ActionName.Should().Be("Login");
        var pedido = api.Pedidos.Should().ContainSingle().Subject;
        pedido.Caminho.Should().EndWith("/auth/reset-password-code");
        pedido.Corpo.Should().Contain("\"codigo\":\"123456\"").And.Contain("\"email\":\"ana@casadababa.com\"")
            .And.Contain("\"novaSenha\":\"Nova@Senha123\"");
    }

    [Fact]
    public async Task TelaDoCodigoMostraAFalhaGenericaELimpaOCodigo()
    {
        var (controller, _) = Montar(HttpStatusCode.BadRequest,
            """{"error":{"code":"BAD_REQUEST","message":"Codigo invalido ou expirado."}}""");
        var vm = new ResetPasswordCodeViewModel
        {
            Email = "ana@casadababa.com", Codigo = "123456", NovaSenha = "Nova@Senha123", ConfirmarSenha = "Nova@Senha123",
        };

        var resultado = await controller.RedefinirSenhaCodigo(vm);

        resultado.Should().BeOfType<ViewResult>();
        vm.Codigo.Should().BeEmpty();
        controller.ModelState.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task PedidoDeRedefinicaoMandaSoOEmail()
    {
        var (controller, api) = Montar(HttpStatusCode.Accepted);

        await controller.EsqueciSenha(new ForgotPasswordViewModel { Email = "ana@casadababa.com" });

        var pedido = api.Pedidos.Should().ContainSingle().Subject;
        pedido.Caminho.Should().EndWith("/auth/forgot-password");
        pedido.Corpo.Should().Contain("ana@casadababa.com").And.NotContain("baseUrl");
    }
}
