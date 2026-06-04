using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace EasyStock.Web.UnitTests.Integration;

/// <summary>
/// BUG-65 (#452): um endpoint protegido acessado sem cookie deve responder de forma
/// JSON-friendly (401) a requisições AJAX (X-Requested-With ou Accept: application/json)
/// e continuar redirecionando (302) navegação de documento. Prova o wiring real do
/// cookie-auth (OnRedirectToLogin) in-process, sem depender do sandbox.
/// Endpoint usado: /notificacoes/resumo (deriva de BaseController, [Authorize]).
/// </summary>
public class AuthChallengeJsonTests : IClassFixture<WebApplicationFactory<WebTestEntryPoint>>
{
    private readonly WebApplicationFactory<WebTestEntryPoint> _factory;

    public AuthChallengeJsonTests(WebApplicationFactory<WebTestEntryPoint> factory) => _factory = factory;

    private HttpClient Client() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    [Fact]
    public async Task ProtegidoSemCookie_Navegacao_Redireciona302ParaLogin()
    {
        var req = new HttpRequestMessage(HttpMethod.Get, "/notificacoes/resumo");
        req.Headers.Accept.ParseAdd("text/html");

        var res = await Client().SendAsync(req);

        res.StatusCode.Should().Be(HttpStatusCode.Found);
        res.Headers.Location!.ToString().Should().Contain("/auth/login");
    }

    [Fact]
    public async Task ProtegidoSemCookie_XRequestedWith_Retorna401SemRedirect()
    {
        var req = new HttpRequestMessage(HttpMethod.Get, "/notificacoes/resumo");
        req.Headers.Add("X-Requested-With", "fetch");

        var res = await Client().SendAsync(req);

        res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        res.Headers.Location.Should().BeNull();
    }

    [Fact]
    public async Task ProtegidoSemCookie_AcceptJson_Retorna401()
    {
        var req = new HttpRequestMessage(HttpMethod.Get, "/notificacoes/resumo");
        req.Headers.Accept.ParseAdd("application/json");

        var res = await Client().SendAsync(req);

        res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
