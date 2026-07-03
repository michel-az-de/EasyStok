using System.Net;
using System.Text;
using EasyStock.Admin.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyStock.Admin.UnitTests.Services;

/// <summary>
/// Trava o fluxo de auth do AdminApiClient (issue 820), espelhando o
/// TokenRefreshHandlerTests do Web (issue 796): 401s concorrentes do MESMO refresh
/// token fazem exatamente 1 chamada a auth/refresh (a API rotaciona o token
/// single-use), refresh falho limpa a sessao, rota /auth/ nao recebe Bearer e o
/// X-Forwarded-For do browser e carimbado em toda chamada (incidente 747: sem XFF
/// o rate limiter da API colapsa todos os admins no IP do container).
/// </summary>
public class AdminTokenRefreshHandlerTests
{
    [Fact]
    public async Task Dois_401_concorrentes_fazem_um_unico_refresh_e_ambos_completam()
    {
        var stub = new StubApiHandler(oldAccess: "old-token", newAccess: "new-token", newRefresh: "rt-rotacionado");
        var (session, accessor) = SessaoCom("old-token", "rt-original");

        using var invoker = Invoker(session, accessor, stub);

        var r1 = invoker.SendAsync(Req("api/admin/tenants"), CancellationToken.None);
        var r2 = invoker.SendAsync(Req("api/admin/faturas"), CancellationToken.None);
        var responses = await Task.WhenAll(r1, r2);

        responses.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.OK);
        stub.RefreshCalls.Should().Be(1, "a API rotaciona o refresh token single-use");
        session.GetToken().Should().Be("new-token");
        session.GetRefreshToken().Should().Be("rt-rotacionado");
    }

    [Fact]
    public async Task Refresh_que_falha_limpa_a_sessao_e_devolve_o_401()
    {
        var stub = new StubApiHandler("old-token", "new-token", "x") { FalharRefresh = true };
        var (session, accessor) = SessaoCom("old-token", "rt-original");

        using var invoker = Invoker(session, accessor, stub);
        var resp = await invoker.SendAsync(Req("api/admin/tenants"), CancellationToken.None);

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        session.GetToken().Should().BeNull("a sessao deve ser limpa quando o refresh falha");
        accessor.HttpContext!.Response.Headers.SetCookie.ToString()
            .Should().Contain("_se_admin", "o banner de sessao expirada depende deste cookie");
    }

    [Fact]
    public async Task Rota_de_auth_nao_recebe_Bearer()
    {
        var stub = new ScriptedHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        });
        var (session, accessor) = SessaoCom("token-atual", "rt");

        var handler = new AdminTokenRefreshHandler(session, NullLogger<AdminTokenRefreshHandler>.Instance, accessor)
        {
            InnerHandler = stub
        };
        using var invoker = new HttpMessageInvoker(handler);
        await invoker.SendAsync(Req("api/auth/login"), CancellationToken.None);

        stub.Requests.Single().Headers.Authorization.Should().BeNull("login/refresh nao usam Bearer");
    }

    [Fact]
    public async Task XForwardedFor_do_browser_e_carimbado_na_chamada_de_saida()
    {
        var stub = new ScriptedHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        });
        var (session, accessor) = SessaoCom("token-atual", "rt");
        accessor.HttpContext!.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.7");

        var handler = new AdminTokenRefreshHandler(session, NullLogger<AdminTokenRefreshHandler>.Instance, accessor)
        {
            InnerHandler = stub
        };
        using var invoker = new HttpMessageInvoker(handler);
        await invoker.SendAsync(Req("api/admin/tenants"), CancellationToken.None);

        stub.Requests.Single().Headers.GetValues("X-Forwarded-For").Single()
            .Should().Be("203.0.113.7", "sem XFF o rate limiter da API ve so o IP do container (incidente 747)");
    }

    private static HttpRequestMessage Req(string path)
        => new(HttpMethod.Get, $"http://api.test/{path}");

    private static (AdminSessionService session, IHttpContextAccessor accessor) SessaoCom(string access, string refresh)
    {
        var ctx = new DefaultHttpContext { Session = new FakeSession() };
        var accessor = new FixedHttpContextAccessor(ctx);
        var session = new AdminSessionService(accessor);
        session.SetTokens(access, refresh);
        return (session, accessor);
    }

    private static HttpMessageInvoker Invoker(AdminSessionService session, IHttpContextAccessor accessor, HttpMessageHandler inner)
        => new(new AdminTokenRefreshHandler(session, NullLogger<AdminTokenRefreshHandler>.Instance, accessor)
        {
            InnerHandler = inner
        });

    /// <summary>
    /// API fake: 401 para o token antigo, 200 para o novo; auth/refresh conta chamadas
    /// e demora de proposito para forcar a sobreposicao dos dois callers concorrentes.
    /// </summary>
    private sealed class StubApiHandler(string oldAccess, string newAccess, string newRefresh) : HttpMessageHandler
    {
        private int _refreshCalls;
        public int RefreshCalls => _refreshCalls;
        public bool FalharRefresh { get; init; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri!.OriginalString.Contains("auth/refresh"))
            {
                Interlocked.Increment(ref _refreshCalls);
                await Task.Delay(100, ct);
                if (FalharRefresh)
                    return new HttpResponseMessage(HttpStatusCode.Unauthorized);
                var body = $"{{\"token\":\"{newAccess}\",\"refreshToken\":\"{newRefresh}\"}}";
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json")
                };
            }

            var bearer = request.Headers.Authorization?.Parameter;
            return new HttpResponseMessage(bearer == oldAccess || bearer is null
                ? HttpStatusCode.Unauthorized
                : HttpStatusCode.OK);
        }
    }
}
