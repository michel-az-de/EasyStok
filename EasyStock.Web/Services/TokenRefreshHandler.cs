using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;

namespace EasyStock.Web.Services;

public class TokenRefreshHandler(
    SessionService session,
    ILogger<TokenRefreshHandler> log,
    IHttpContextAccessor httpContextAccessor,
    IJwtClaimsReader jwt) : DelegatingHandler
{
    /// <summary>
    /// Single-flight de refresh keyed pelo refresh token (issue 796). O handler e resolvido
    /// pelo IHttpClientFactory no escopo do pipeline (cacheado ~2min e compartilhado entre
    /// requests/usuarios), entao qualquer estado de instancia vaza entre usuarios. O
    /// dicionario estatico garante: 401s concorrentes do MESMO token compartilham 1 refresh
    /// (a Api rotaciona o token single-use — um segundo refresh derrubaria a sessao) e
    /// tokens diferentes nunca interferem. A entrada fica viva por um curto periodo apos
    /// completar para que retries com sessao ainda-stale reutilizem o resultado.
    /// </summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Lazy<Task<RefreshResult?>>> RefreshFlights = new();
    private static readonly TimeSpan FlightRetention = TimeSpan.FromSeconds(30);

    private sealed record RefreshResult(string AccessToken, string? RefreshToken, string? EmpresaId);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        // Repassa o IP REAL do browser para a API em TODA chamada (inclusive /auth/login e
        // /auth/refresh). O Web e um BFF: sem isto a API ve so o IP do container web e o rate
        // limiter de auth colapsa todos os usuarios numa unica particao (mesma causa do
        // incidente login-admin). Ver EasyStock.Api/Program.cs (ForwardedHeaders) + #277/#657.
        ApplyForwardedFor(request);

        var isAuthRoute = request.RequestUri?.PathAndQuery.Contains("/auth/", StringComparison.OrdinalIgnoreCase) == true;
        var token = session.GetToken();
        var lojaId = session.GetLojaId();
        var empresaId = session.GetEmpresaId();
        HttpRequestMessage? retryRequest = null;

        if (!isAuthRoute && !string.IsNullOrEmpty(token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            retryRequest = await CloneRequestAsync(request, ct);

            if (!string.IsNullOrEmpty(lojaId))
            {
                request.Headers.TryAddWithoutValidation("X-Loja-ID", lojaId);
                retryRequest!.Headers.TryAddWithoutValidation("X-Loja-ID", lojaId);
            }

            if (!string.IsNullOrEmpty(empresaId))
            {
                // So a request original: o retry recebe o empresaId ATUAL da sessao apos o
                // refresh (o novo JWT pode carregar outro empresaId — issue 796).
                request.RequestUri = AddQueryString(request.RequestUri, "empresaId", empresaId);
            }
        }

        var response = await base.SendAsync(request, ct);

        if (response.StatusCode == HttpStatusCode.Unauthorized && !isAuthRoute)
        {
            var refreshToken = session.GetRefreshToken();
            if (!string.IsNullOrEmpty(refreshToken))
            {
                var result = await RefreshSingleFlightAsync(refreshToken);
                if (result is not null && retryRequest is not null)
                {
                    // Cada request concorrente aplica o resultado na PROPRIA copia da
                    // sessao — reduz a janela de um commit stale sobrescrever os tokens.
                    session.SetTokens(result.AccessToken, result.RefreshToken ?? refreshToken);
                    if (!string.IsNullOrEmpty(result.EmpresaId))
                        session.SetEmpresaId(result.EmpresaId);

                    retryRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", result.AccessToken);
                    var empresaAtual = session.GetEmpresaId();
                    if (!string.IsNullOrEmpty(empresaAtual))
                        retryRequest.RequestUri = AddQueryString(retryRequest.RequestUri, "empresaId", empresaAtual);
                    response.Dispose();
                    response = await base.SendAsync(retryRequest, ct);
                }
                else
                {
                    log.LogWarning("Token refresh failed — clearing session");
                    MarkSessionExpired();
                    session.Clear();
                }
            }
            else
            {
                MarkSessionExpired();
                session.Clear();
            }
        }

        return response;
    }

    private async Task<RefreshResult?> RefreshSingleFlightAsync(string refreshToken)
    {
        var flight = new Lazy<Task<RefreshResult?>>(
            // CancellationToken.None: o flight e compartilhado — o cancel de um caller
            // nao pode envenenar o refresh dos demais.
            () => TryRefreshAsync(refreshToken, CancellationToken.None),
            LazyThreadSafetyMode.ExecutionAndPublication);
        var existing = RefreshFlights.GetOrAdd(refreshToken, flight);
        if (ReferenceEquals(existing, flight))
        {
            // Criador agenda a remocao apos retencao curta (retries stale reutilizam o resultado).
            _ = CleanupFlightAsync(refreshToken, flight.Value);
        }
        return await existing.Value;
    }

    private static async Task CleanupFlightAsync(string refreshToken, Task flightTask)
    {
        try { await flightTask; } catch { /* falha do refresh e tratada nos callers */ }
        await Task.Delay(FlightRetention);
        RefreshFlights.TryRemove(refreshToken, out _);
    }

    /// <summary>
    /// Carimba o X-Forwarded-For da request de saida com o IP REAL do browser (entrada mais a
    /// direita do X-Forwarded-For que o Caddy injetou — resistente a spoof — com fallback pro
    /// RemoteIpAddress). Single-entry: a API (ForwardLimit=1) consome exatamente este IP.
    /// </summary>
    private void ApplyForwardedFor(HttpRequestMessage request)
    {
        try
        {
            var ctx = httpContextAccessor.HttpContext;
            if (ctx is null) return;

            string? clientIp = null;
            var xff = ctx.Request.Headers["X-Forwarded-For"].ToString();
            if (!string.IsNullOrWhiteSpace(xff))
                clientIp = xff.Split(',').Select(s => s.Trim()).LastOrDefault(s => s.Length > 0);
            clientIp ??= ctx.Connection.RemoteIpAddress?.ToString();
            if (string.IsNullOrWhiteSpace(clientIp)) return;

            request.Headers.Remove("X-Forwarded-For");
            request.Headers.TryAddWithoutValidation("X-Forwarded-For", clientIp);
        }
        catch
        {
            // Best-effort: nunca quebrar a chamada por causa do header de IP.
        }
    }

    private void MarkSessionExpired()
    {
        try
        {
            var ctx = httpContextAccessor.HttpContext;
            if (ctx is null) return;
            ctx.Response.Cookies.Append(
                "_se", "1",
                new CookieOptions
                {
                    HttpOnly = true,
                    Secure = ctx.Request.IsHttps,  // obrigatório em HTTPS (produção)
                    Path = "/",
                    Expires = DateTimeOffset.UtcNow.AddSeconds(30),
                    SameSite = SameSiteMode.Strict
                });
        }
        catch
        {
            // Não bloquear o fluxo se o cookie não puder ser escrito
        }
    }

    private async Task<RefreshResult?> TryRefreshAsync(string refreshToken, CancellationToken ct)
    {
        try
        {
            var body = JsonSerializer.Serialize(new { refreshToken });
            var content = new StringContent(body, Encoding.UTF8, "application/json");

            // Use inner handler directly to avoid re-entering this handler
            var refreshRequest = new HttpRequestMessage(HttpMethod.Post, "auth/refresh") { Content = content };
            // Enviado via base.SendAsync (nao re-entra em SendAsync) -> carimba o X-Forwarded-For aqui.
            ApplyForwardedFor(refreshRequest);
            var resp = await base.SendAsync(refreshRequest, ct);

            if (!resp.IsSuccessStatusCode) return null;

            var json = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);

            // Response may be { data: { token, refreshToken } } or { token, refreshToken }
            JsonElement root = doc.RootElement;
            if (root.TryGetProperty("data", out var data))
                root = data;

            var newAccess = root.TryGetProperty("token", out var t) ? t.GetString() : null;
            var newRefresh = root.TryGetProperty("refreshToken", out var rt) ? rt.GetString() : null;

            if (!string.IsNullOrEmpty(newAccess))
            {
                // Sincroniza empresa_atual_id com o novo JWT para que GetEmpresaId()
                // nas services continue correto mesmo se o token anterior estava
                // expirado e o novo token carrega um empresaId diferente/novo.
                // (A escrita na sessao acontece no caller — cada request concorrente
                // aplica na propria copia da sessao.)
                var newEmpresaId = jwt.TryReadClaim(newAccess, "empresaId");
                return new RefreshResult(newAccess, newRefresh, newEmpresaId);
            }

            return null;
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Exception during token refresh");
            return null;
        }
    }

    // ExtractClaim removido — consolidado em IJwtClaimsReader (TASK-EZ-WEB-005).

    internal static Uri? AddQueryString(Uri? uri, string key, string value)
    {
        if (uri is null) return null;

        // Evita duplicar parâmetros que o serviço já incluiu na URL
        var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
        if (query[key] is not null) return uri;

        var updated = QueryHelpers.AddQueryString(uri.ToString(), key, value);
        return new Uri(updated, uri.IsAbsoluteUri ? UriKind.Absolute : UriKind.Relative);
    }

    private static async Task<HttpRequestMessage> CloneRequestAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri)
        {
            Version = request.Version
        };

        foreach (var header in request.Headers)
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);

        if (request.Content is not null)
        {
            var ms = new MemoryStream();
            await request.Content.CopyToAsync(ms, ct);
            ms.Position = 0;

            var content = new StreamContent(ms);
            foreach (var header in request.Content.Headers)
                content.Headers.TryAddWithoutValidation(header.Key, header.Value);

            clone.Content = content;
        }

        foreach (var option in request.Options)
            clone.Options.Set(new HttpRequestOptionsKey<object?>(option.Key), option.Value);

        return clone;
    }
}
