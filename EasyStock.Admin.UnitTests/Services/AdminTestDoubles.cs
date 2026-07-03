using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;

namespace EasyStock.Admin.UnitTests.Services;

/// <summary>
/// Dobles compartilhados dos testes de Services/Middleware do Admin (issue 820).
/// Espelham os do EasyStock.Web.UnitTests/Services/TokenRefreshHandlerTests.cs.
/// </summary>
internal static class AdminTestSupport
{
    /// <summary>JWT fake (payload base64url) — o middleware decodifica sem validar assinatura.</summary>
    public static string JwtCom(string nivel, string nome = "Tester", string email = "t@easystok.com")
    {
        var payload = $"{{\"nivel\":\"{nivel}\",\"nome\":\"{nome}\",\"email\":\"{email}\"}}";
        var b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(payload))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return $"eyJoZWFkZXIi.{b64}.assinatura";
    }
}

/// <summary>
/// IHttpContextAccessor de valor fixo. O HttpContextAccessor real guarda o contexto
/// em AsyncLocal — setar dentro de um helper async nao flui de volta pro teste.
/// </summary>
internal sealed class FixedHttpContextAccessor(HttpContext ctx) : IHttpContextAccessor
{
    public HttpContext? HttpContext { get; set; } = ctx;
}

internal sealed class FakeSession : ISession
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

internal sealed class FakeWebHostEnvironment : IWebHostEnvironment
{
    public string EnvironmentName { get; set; } = "Production";
    public string ApplicationName { get; set; } = "EasyStock.Admin";
    public string WebRootPath { get; set; } = "";
    public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    public string ContentRootPath { get; set; } = "";
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}

/// <summary>Handler scriptado: devolve o que a funcao mandar e conta chamadas por rota.</summary>
internal sealed class ScriptedHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    private int _calls;
    public int Calls => _calls;
    public List<HttpRequestMessage> Requests { get; } = new();

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Interlocked.Increment(ref _calls);
        lock (Requests) Requests.Add(request);
        return Task.FromResult(respond(request));
    }
}
