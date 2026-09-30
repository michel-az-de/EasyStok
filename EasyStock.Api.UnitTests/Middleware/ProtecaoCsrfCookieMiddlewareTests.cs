using EasyStock.Api.Middleware;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyStock.Api.UnitTests.Middleware;

/// <summary>
/// Issue #1088 (ADR-0053): request que altera estado e carrega cookie do storefront precisa vir da
/// mesma origem. Bearer cross-origin (Web, Admin, Capacitor) nao carrega o cookie e segue livre.
/// </summary>
public class ProtecaoCsrfCookieMiddlewareTests
{
    private const string CookieSessao = "__Host-cdb_session=6f1c1b7e-8a51-4c1e-9a55-0d7f3c1f2a10";

    private static (ProtecaoCsrfCookieMiddleware mw, int[] chamadas) Build()
    {
        var chamadas = new int[1];
        var mw = new ProtecaoCsrfCookieMiddleware(
            _ => { chamadas[0]++; return Task.CompletedTask; },
            NullLogger<ProtecaoCsrfCookieMiddleware>.Instance);
        return (mw, chamadas);
    }

    private static DefaultHttpContext Ctx(
        string method = "POST",
        string? cookie = CookieSessao,
        string? secFetchSite = null,
        string? origin = null,
        string host = "loja.casadababa.com.br")
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Method = method;
        ctx.Request.Path = "/api/storefront/casa-da-baba/checkout";
        ctx.Request.Host = new HostString(host);
        if (cookie is not null) ctx.Request.Headers.Cookie = cookie;
        if (secFetchSite is not null) ctx.Request.Headers["Sec-Fetch-Site"] = secFetchSite;
        if (origin is not null) ctx.Request.Headers.Origin = origin;
        ctx.Response.Body = new MemoryStream();
        return ctx;
    }

    [Theory]
    [InlineData("cross-site")]
    [InlineData("same-site")]
    public async Task Post_com_cookie_vindo_de_outro_site_recebe_403(string secFetchSite)
    {
        var (mw, chamadas) = Build();
        var ctx = Ctx(secFetchSite: secFetchSite);

        await mw.InvokeAsync(ctx);

        ctx.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        ctx.Response.ContentType.Should().StartWith("application/problem+json");
        chamadas[0].Should().Be(0);
    }

    [Theory]
    [InlineData("same-origin")]
    [InlineData("none")]
    public async Task Post_com_cookie_da_mesma_origem_segue(string secFetchSite)
    {
        var (mw, chamadas) = Build();

        await mw.InvokeAsync(Ctx(secFetchSite: secFetchSite));

        chamadas[0].Should().Be(1);
    }

    [Theory]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    public async Task Outros_metodos_que_alteram_estado_tambem_sao_barrados(string method)
    {
        var (mw, chamadas) = Build();

        await mw.InvokeAsync(Ctx(method: method, secFetchSite: "cross-site"));

        chamadas[0].Should().Be(0);
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("HEAD")]
    [InlineData("OPTIONS")]
    public async Task Metodo_seguro_nao_e_verificado(string method)
    {
        var (mw, chamadas) = Build();

        await mw.InvokeAsync(Ctx(method: method, secFetchSite: "cross-site"));

        chamadas[0].Should().Be(1);
    }

    [Fact]
    public async Task Post_sem_cookie_do_storefront_segue_mesmo_cross_site()
    {
        // Web, Admin e Capacitor chamam a Api por bearer, de outra origem, sem o cookie.
        var (mw, chamadas) = Build();

        await mw.InvokeAsync(Ctx(cookie: "outro=1", secFetchSite: "cross-site", origin: "https://easystok-web.onrender.com"));

        chamadas[0].Should().Be(1);
    }

    [Fact]
    public async Task Cookie_de_avaliacao_tambem_exige_mesma_origem()
    {
        var (mw, chamadas) = Build();

        await mw.InvokeAsync(Ctx(cookie: "__Host-cdb_aval_3f2a=abc", secFetchSite: "cross-site"));

        chamadas[0].Should().Be(0);
    }

    [Fact]
    public async Task Sem_fetch_metadata_aceita_origin_igual_ao_host()
    {
        var (mw, chamadas) = Build();

        await mw.InvokeAsync(Ctx(origin: "https://loja.casadababa.com.br"));

        chamadas[0].Should().Be(1);
    }

    [Theory]
    [InlineData("https://atacante.example")]
    [InlineData("https://sub.casadababa.com.br")]
    [InlineData("null")]
    public async Task Sem_fetch_metadata_recusa_origin_diferente_do_host(string origin)
    {
        var (mw, chamadas) = Build();
        var ctx = Ctx(origin: origin);

        await mw.InvokeAsync(ctx);

        ctx.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        chamadas[0].Should().Be(0);
    }

    [Fact]
    public async Task Log_da_recusa_nao_leva_quebra_de_linha_vinda_do_cliente()
    {
        // #1098: Origin e Sec-Fetch-Site vem do cliente; com \r\n fabricariam linha no log.
        var logger = new CapturingLogger<ProtecaoCsrfCookieMiddleware>();
        var mw = new ProtecaoCsrfCookieMiddleware(_ => Task.CompletedTask, logger);
        var ctx = Ctx(secFetchSite: "cross-site\r\nFAKE", origin: "https://x.example\r\nINFO forjado");

        await mw.InvokeAsync(ctx);

        logger.Entries.Should().ContainSingle()
            .Which.Should().NotContainAny("\r", "\n");
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<string> Entries { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
            => Entries.Add(formatter(state, exception));
    }

    [Fact]
    public async Task Sem_fetch_metadata_e_sem_origin_segue()
    {
        // Navegador sempre manda Origin em POST; sem os dois, e cliente que nao anexa cookie
        // sozinho (curl, app), e CSRF nao se aplica.
        var (mw, chamadas) = Build();

        await mw.InvokeAsync(Ctx());

        chamadas[0].Should().Be(1);
    }
}
