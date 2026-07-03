using System.Net;
using System.Text;
using EasyStock.Admin.Middleware;
using EasyStock.Admin.Services;
using EasyStock.Admin.UnitTests.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyStock.Admin.UnitTests.Middleware;

/// <summary>
/// Trava o self-heal de sessao pos-deploy (issue 820): o cookie _rt_admin restaura a
/// sessao in-memory zerada; 401 (token rejeitado) apaga o cookie; 429 e API-fora
/// PRESERVAM o cookie (incidente 747: re-tentar batendo no rate limiter amplifica o
/// flood); token sem nivel=SuperAdmin nao vira sessao (defesa em profundidade).
/// </summary>
public class AdminSessionRestoreMiddlewareTests
{
    [Fact]
    public async Task Cookie_valido_restaura_sessao_e_rotaciona_o_rt_admin()
    {
        var jwt = AdminTestSupport.JwtCom("SuperAdmin", nome: "Ana");
        var body = $"{{\"data\":{{\"token\":\"{jwt}\",\"refreshToken\":\"rt-novo\"}}}}";
        var stub = new ScriptedHandler(_ => Json(HttpStatusCode.OK, body));

        var (ctx, session) = await Invocar(stub, cookie: "rt-antigo");

        session.GetToken().Should().Be(jwt);
        session.GetNome().Should().Be("Ana");
        SetCookies(ctx).Should().Contain(c => c.StartsWith("_rt_admin=rt-novo"), "refresh rotacionado deve ser persistido");
    }

    [Fact]
    public async Task Refresh_401_apaga_o_cookie_e_sinaliza_expiracao()
    {
        var stub = new ScriptedHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));

        var (ctx, session) = await Invocar(stub, cookie: "rt-rejeitado");

        session.GetToken().Should().BeNull();
        SetCookies(ctx).Should().Contain(c => c.StartsWith("_rt_admin=;"), "401 e definitivo: o cookie morre");
        SetCookies(ctx).Should().Contain(c => c.StartsWith("_se_admin="), "banner de sessao expirada");
    }

    [Fact]
    public async Task Rate_limit_429_preserva_o_cookie_sem_re_tentar()
    {
        var stub = new ScriptedHandler(_ =>
            Json((HttpStatusCode)429, "{\"error\":{\"code\":\"RATE_LIMIT_EXCEEDED\"}}"));

        var (ctx, session) = await Invocar(stub, cookie: "rt-preservado");

        session.GetToken().Should().BeNull();
        stub.Calls.Should().Be(1, "re-tentar contra o rate limiter amplifica o flood (incidente 747)");
        SetCookies(ctx).Should().NotContain(c => c.StartsWith("_rt_admin=;"), "429 e transitorio: cookie sobrevive p/ self-heal");
    }

    [Fact]
    public async Task Token_sem_nivel_SuperAdmin_nao_vira_sessao()
    {
        var jwt = AdminTestSupport.JwtCom("Admin");
        var body = $"{{\"data\":{{\"token\":\"{jwt}\",\"refreshToken\":\"rt-novo\"}}}}";
        var stub = new ScriptedHandler(_ => Json(HttpStatusCode.OK, body));

        var (ctx, session) = await Invocar(stub, cookie: "rt-qualquer");

        session.GetToken().Should().BeNull("defesa em profundidade: so SuperAdmin restaura sessao");
        SetCookies(ctx).Should().Contain(c => c.StartsWith("_rt_admin=;"), "token de nivel errado e definitivo");
    }

    [Fact]
    public async Task API_sem_token_re_tenta_no_maximo_2_vezes_e_preserva_o_cookie()
    {
        // Envelope de erro generico (nao rate-limit, sem token) -> loop de retry.
        var stub = new ScriptedHandler(_ =>
            Json(HttpStatusCode.OK, "{\"error\":{\"code\":\"X\",\"message\":\"api reiniciando\"}}"));

        var (ctx, _) = await Invocar(stub, cookie: "rt-preservado");

        stub.Calls.Should().Be(2, "issue 819: 2 tentativas, nao 3 — cada uma pode custar o timeout inteiro");
        SetCookies(ctx).Should().NotContain(c => c.StartsWith("_rt_admin=;"), "transitorio: cookie sobrevive");
    }

    [Fact]
    public async Task Sem_cookie_nao_chama_a_API()
    {
        var stub = new ScriptedHandler(_ => Json(HttpStatusCode.OK, "{}"));

        var (_, session) = await Invocar(stub, cookie: null);

        stub.Calls.Should().Be(0);
        session.GetToken().Should().BeNull();
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
    };

    private static IEnumerable<string> SetCookies(HttpContext ctx)
        => ctx.Response.Headers.SetCookie.Select(v => v?.ToString() ?? "");

    private static async Task<(HttpContext ctx, AdminSessionService session)> Invocar(ScriptedHandler stub, string? cookie)
    {
        var ctx = new DefaultHttpContext { Session = new FakeSession() };
        ctx.Request.Path = "/Tenants";
        if (cookie is not null)
            ctx.Request.Headers.Cookie = $"_rt_admin={cookie}";

        var session = new AdminSessionService(new FixedHttpContextAccessor(ctx));
        var api = new AdminApiClient(new HttpClient(stub) { BaseAddress = new Uri("http://api.test/") });

        var mw = new AdminSessionRestoreMiddleware(
            _ => Task.CompletedTask,
            NullLogger<AdminSessionRestoreMiddleware>.Instance,
            new FakeWebHostEnvironment());

        await mw.InvokeAsync(ctx, session, api);
        return (ctx, session);
    }
}
