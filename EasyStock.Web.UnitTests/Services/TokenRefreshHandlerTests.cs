using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Text;
using EasyStock.Web.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyStock.Web.UnitTests.Services;

/// <summary>
/// Trava o single-flight do refresh (issue 796): o handler e compartilhado entre
/// requests pelo pool do IHttpClientFactory, entao 401s concorrentes do MESMO
/// refresh token devem disparar exatamente 1 chamada a auth/refresh (a Api rotaciona
/// o token single-use — um segundo refresh derrubaria a sessao) e ambas as requests
/// devem completar com sucesso no retry.
/// </summary>
public class TokenRefreshHandlerTests
{
    [Fact]
    public async Task Dois_401_concorrentes_do_mesmo_token_fazem_um_unico_refresh_e_ambos_completam()
    {
        var refreshToken = $"rt-{Guid.NewGuid():N}";
        var stub = new StubApiHandler(oldAccess: "old-token", newAccess: "new-token", newRefresh: "rt-rotacionado");
        var session = SessaoCom("old-token", refreshToken, out var accessor);

        var handler = new TokenRefreshHandler(
            session, NullLogger<TokenRefreshHandler>.Instance, accessor, new JwtClaimsReader())
        {
            InnerHandler = stub
        };
        using var invoker = new HttpMessageInvoker(handler);

        var r1 = invoker.SendAsync(Req(), CancellationToken.None);
        var r2 = invoker.SendAsync(Req(), CancellationToken.None);
        var responses = await Task.WhenAll(r1, r2);

        responses.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.OK);
        stub.RefreshCalls.Should().Be(1, "a Api rotaciona o refresh token single-use");
        session.GetToken().Should().Be("new-token");
        session.GetRefreshToken().Should().Be("rt-rotacionado");
    }

    [Fact]
    public async Task Refresh_que_falha_limpa_a_sessao_e_devolve_o_401()
    {
        var refreshToken = $"rt-{Guid.NewGuid():N}";
        var stub = new StubApiHandler(oldAccess: "old-token", newAccess: "new-token", newRefresh: "x")
        {
            FalharRefresh = true
        };
        var session = SessaoCom("old-token", refreshToken, out var accessor);

        var handler = new TokenRefreshHandler(
            session, NullLogger<TokenRefreshHandler>.Instance, accessor, new JwtClaimsReader())
        {
            InnerHandler = stub
        };
        using var invoker = new HttpMessageInvoker(handler);

        var resp = await invoker.SendAsync(Req(), CancellationToken.None);

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        session.GetToken().Should().BeNull("a sessao deve ser limpa quando o refresh falha");
    }

    // #846: o retry apos refresh reenvia o CLONE do request. Para POST/PUT com corpo, o
    // CloneRequestAsync bufferiza e rebobina o content — este teste trava que o corpo sobrevive
    // (o teste de race acima usa GET sem corpo, entao nao cobria o clone de content).
    [Fact]
    public async Task Retry_de_POST_apos_refresh_reenvia_o_corpo_intacto()
    {
        var refreshToken = $"rt-{Guid.NewGuid():N}";
        var stub = new RetryBodyHandler();
        var session = SessaoCom("old-token", refreshToken, out var accessor);

        var handler = new TokenRefreshHandler(
            session, NullLogger<TokenRefreshHandler>.Instance, accessor, new JwtClaimsReader())
        {
            InnerHandler = stub
        };
        using var invoker = new HttpMessageInvoker(handler);

        var req = new HttpRequestMessage(HttpMethod.Post, "http://api.test/minha-vitrine/cardapio")
        {
            Content = new StringContent("{\"nomePublico\":\"Lasanha\"}", Encoding.UTF8, "application/json")
        };
        var resp = await invoker.SendAsync(req, CancellationToken.None);

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        stub.RetryBody.Should().Be("{\"nomePublico\":\"Lasanha\"}",
            "o corpo do POST deve sobreviver ao clone e ao retry apos o refresh");
    }

    private static HttpRequestMessage Req()
        => new(HttpMethod.Get, "http://api.test/produtos/listar");

    private static SessionService SessaoCom(string access, string refresh, out IHttpContextAccessor accessor)
    {
        var ctx = new DefaultHttpContext { Session = new FakeSession() };
        accessor = new HttpContextAccessor { HttpContext = ctx };
        var session = new SessionService(accessor);
        session.SetTokens(access, refresh);
        return session;
    }

    /// <summary>
    /// Api fake: 401 para o token antigo, 200 para o novo; auth/refresh conta chamadas
    /// e demora de proposito para forcar a sobreposicao dos dois callers.
    /// </summary>
    private sealed class StubApiHandler(string oldAccess, string newAccess, string newRefresh) : HttpMessageHandler
    {
        private int _refreshCalls;
        public int RefreshCalls => _refreshCalls;
        public bool FalharRefresh { get; init; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            // OriginalString: o refresh sai como URI relativa ("auth/refresh") — em producao
            // o HttpClient resolve contra o BaseAddress, mas o HttpMessageInvoker do teste nao.
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

    /// <summary>
    /// Api fake para o retry de POST: 401 no token antigo, refresh OK, e no retry (token novo)
    /// captura o corpo recebido — prova que o clone preservou o body atraves do refresh.
    /// </summary>
    private sealed class RetryBodyHandler : HttpMessageHandler
    {
        public string? RetryBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri!.OriginalString.Contains("auth/refresh"))
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        "{\"token\":\"new-token\",\"refreshToken\":\"rt-novo\"}", Encoding.UTF8, "application/json")
                };

            var bearer = request.Headers.Authorization?.Parameter;
            if (bearer is null || bearer == "old-token")
                return new HttpResponseMessage(HttpStatusCode.Unauthorized);

            // Retry com o token novo: captura o corpo reenviado.
            if (request.Content is not null)
                RetryBody = await request.Content.ReadAsStringAsync(ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    private sealed class FakeSession : ISession
    {
        private readonly ConcurrentDictionary<string, byte[]> _store = new();
        public bool IsAvailable => true;
        public string Id { get; } = Guid.NewGuid().ToString("N");
        public IEnumerable<string> Keys => _store.Keys;
        public void Clear() => _store.Clear();
        public Task CommitAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task LoadAsync(CancellationToken ct = default) => Task.CompletedTask;
        public void Remove(string key) => _store.TryRemove(key, out _);
        public void Set(string key, byte[] value) => _store[key] = value;
        // [NotNullWhen] casa com a anotacao do ISession — sem isso o CI (-warnaserror) da CS8767.
        public bool TryGetValue(string key, [NotNullWhen(true)] out byte[]? value) => _store.TryGetValue(key, out value!);
    }
}
