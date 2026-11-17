using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Security.Claims;
using System.Text;
using EasyStock.Web.Middleware;
using EasyStock.Web.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace EasyStock.Web.UnitTests.Middleware;

/// <summary>
/// #2745: a Api consome o refresh uma vez só. Depois de um deploy, requisições paralelas do mesmo navegador
/// chegam com a sessão vazia e o mesmo cookie _rt; se cada uma renovasse, a perdedora derrubaria o login.
/// </summary>
public class SessionRestoreMiddlewareTests
{
    [Fact]
    public async Task Restauracoes_paralelas_do_mesmo_cookie_fazem_uma_so_renovacao_e_nenhuma_desloga()
    {
        var refresh = $"rt-{Guid.NewGuid():N}";
        var api = new ApiDeUsoUnico(refresh);
        var auth = Substitute.For<IAuthenticationService>();
        var env = Substitute.For<IWebHostEnvironment>();
        env.EnvironmentName.Returns("Development");
        var middleware = new SessionRestoreMiddleware(_ => Task.CompletedTask,
            NullLogger<SessionRestoreMiddleware>.Instance, env);
        var requisicoes = Enumerable.Range(0, 3).Select(_ => Requisicao(refresh, auth)).ToArray();

        await Task.WhenAll(requisicoes.Select(r => middleware.InvokeAsync(r.Contexto, r.Sessao,
            new ApiClient(new HttpClient(api, disposeHandler: false) { BaseAddress = new Uri("http://api.test/") },
                NullLogger<ApiClient>.Instance), new JwtClaimsReader())));

        api.Renovacoes.Should().Be(1, "a Api recusa o segundo uso do mesmo refresh");
        await auth.DidNotReceiveWithAnyArgs().SignOutAsync(default!, default, default);
        requisicoes.Should().OnlyContain(r => r.Sessao.GetToken() == "token-novo");
    }

    private static (HttpContext Contexto, SessionService Sessao) Requisicao(string refresh, IAuthenticationService auth)
    {
        var contexto = new DefaultHttpContext { Session = new FakeSession() };
        contexto.Request.Path = "/dashboard";
        contexto.Request.Headers.Cookie = $"_rt={refresh}";
        contexto.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "u")], "Cookies"));
        var services = new ServiceCollection();
        services.AddSingleton(auth);
        contexto.RequestServices = services.BuildServiceProvider();
        return (contexto, new SessionService(new HttpContextAccessor { HttpContext = contexto }));
    }

    /// <summary>Api fake de uso único: o primeiro auth/refresh devolve o par novo (com atraso, para sobrepor); os outros, 401.</summary>
    private sealed class ApiDeUsoUnico(string refresh) : HttpMessageHandler
    {
        private int _renovacoes;
        public int Renovacoes => _renovacoes;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (!request.RequestUri!.AbsolutePath.EndsWith("auth/refresh"))
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            var corpo = await request.Content!.ReadAsStringAsync(ct);
            var primeira = Interlocked.Increment(ref _renovacoes) == 1;
            await Task.Delay(100, ct);
            if (!primeira || !corpo.Contains(refresh))
                return new HttpResponseMessage(HttpStatusCode.Unauthorized);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"token\":\"token-novo\",\"refreshToken\":\"rt-sucessor\"}",
                    Encoding.UTF8, "application/json")
            };
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
        public bool TryGetValue(string key, [NotNullWhen(true)] out byte[]? value) => _store.TryGetValue(key, out value!);
    }
}
