using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace EasyStock.Web.UnitTests.Integration;

/// <summary>
/// #1007: o Launcher chegou reivindicando <c>[HttpGet("/")]</c>, template que o
/// <c>SiteController</c> ja declara. Dois attribute routes iguais e mesma precedencia
/// derrubam <c>GET /</c> com AmbiguousMatchException — inclusive para visitante anonimo,
/// ou seja, a landing publica inteira. Estes testes provam o roteamento real in-process:
/// a raiz continua anonima e o portal continua protegido.
/// </summary>
public class LauncherRotasTests : IClassFixture<WebApplicationFactory<WebTestEntryPoint>>
{
    private readonly WebApplicationFactory<WebTestEntryPoint> _factory;

    public LauncherRotasTests(WebApplicationFactory<WebTestEntryPoint> factory) => _factory = factory;

    private HttpClient Client() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    [Fact]
    public async Task Raiz_anonima_serve_a_landing_sem_rota_ambigua()
    {
        var req = new HttpRequestMessage(HttpMethod.Get, "/");
        req.Headers.Accept.ParseAdd("text/html");

        var res = await Client().SendAsync(req);

        // 500 aqui = colisao de rota de volta. 302 = a landing virou area logada.
        res.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Launcher_sem_cookie_redireciona_para_login()
    {
        var req = new HttpRequestMessage(HttpMethod.Get, "/launcher");
        req.Headers.Accept.ParseAdd("text/html");

        var res = await Client().SendAsync(req);

        res.StatusCode.Should().Be(HttpStatusCode.Found);
        res.Headers.Location!.ToString().Should().Contain("/auth/login");
    }

    [Fact]
    public async Task Dashboard_continua_existindo_e_protegido()
    {
        var req = new HttpRequestMessage(HttpMethod.Get, "/dashboard");
        req.Headers.Accept.ParseAdd("text/html");

        var res = await Client().SendAsync(req);

        res.StatusCode.Should().Be(HttpStatusCode.Found);
        res.Headers.Location!.ToString().Should().Contain("/auth/login");
    }
}
