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
/// N9: abrir o link do convite (GET, inclusive por scanner de e-mail) nunca chama a API e nunca consome. Quem consome é o
/// POST, que manda só o token e a senha. O token não vaza pelo <c>Referer</c>.
/// </summary>
public class AuthConviteTests
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
        var controller = new AuthController(
            api, new SessionService(Substitute.For<IHttpContextAccessor>()), Substitute.For<IWebHostEnvironment>(),
            Substitute.For<IJwtClaimsReader>())
        {
            ControllerContext = new ControllerContext { HttpContext = http },
            TempData = new TempDataDictionary(http, Substitute.For<ITempDataProvider>()),
        };
        return (controller, handler);
    }

    [Fact]
    public void AbrirOLinkNaoChamaAApi()
    {
        var (controller, api) = Montar();

        var resultado = controller.Convite("token-do-convite");

        resultado.Should().BeOfType<ViewResult>().Which.Model.Should().BeOfType<AceitarConviteViewModel>()
            .Which.Token.Should().Be("token-do-convite");
        api.Pedidos.Should().BeEmpty("o GET so mostra o formulario: nada e consumido, nem por scanner de e-mail");
        controller.Response.Headers["Referrer-Policy"].ToString().Should().Be("no-referrer");
    }

    [Fact]
    public void AbrirOLinkSemTokenNaQueryMostraOFormularioQueLeOFragmento()
    {
        var (controller, api) = Montar();

        var resultado = controller.Convite((string?)null);

        resultado.Should().BeOfType<ViewResult>("o token pode vir no fragmento (#t=), que o servidor nunca recebe");
        api.Pedidos.Should().BeEmpty();
    }

    [Fact]
    public async Task PostDoAceiteChamaAApiComTokenESenha()
    {
        var (controller, api) = Montar();

        var resultado = await controller.Convite(new AceitarConviteViewModel
        {
            Token = "token-do-convite", NovaSenha = "Nova@Senha123", ConfirmarSenha = "Nova@Senha123",
        });

        resultado.Should().BeOfType<RedirectToActionResult>().Which.ActionName.Should().Be("Login");
        var pedido = api.Pedidos.Should().ContainSingle().Subject;
        pedido.Caminho.Should().EndWith("/auth/aceitar-convite");
        pedido.Corpo.Should().Contain("\"token\":\"token-do-convite\"").And.Contain("\"novaSenha\":\"Nova@Senha123\"");
        controller.TempData["Toast"]!.ToString().Should().StartWith("success|");
    }

    [Fact]
    public async Task ConviteInvalidoMostraAMensagemDaApiEMantemOFormulario()
    {
        var (controller, _) = Montar(HttpStatusCode.UnprocessableEntity,
            """{"error":{"code":"DOMAIN_RULE_VIOLATION","message":"Convite inválido ou expirado."}}""");

        var resultado = await controller.Convite(new AceitarConviteViewModel
        {
            Token = "vencido", NovaSenha = "Nova@Senha123", ConfirmarSenha = "Nova@Senha123",
        });

        resultado.Should().BeOfType<ViewResult>();
        controller.ModelState.IsValid.Should().BeFalse();
        controller.ModelState[string.Empty]!.Errors.Single().ErrorMessage.Should().Contain("Convite");
    }

    [Fact]
    public async Task SenhasDiferentesNaoChamamAApi()
    {
        var (controller, api) = Montar();

        controller.ModelState.AddModelError("ConfirmarSenha", "As senhas não coincidem");
        var resultado = await controller.Convite(new AceitarConviteViewModel
        {
            Token = "token", NovaSenha = "Nova@Senha123", ConfirmarSenha = "Outra@Senha456",
        });

        resultado.Should().BeOfType<ViewResult>();
        api.Pedidos.Should().BeEmpty();
    }
}
