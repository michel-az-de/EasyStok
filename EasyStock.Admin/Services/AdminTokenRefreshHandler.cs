using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace EasyStock.Admin.Services;

/// <summary>
/// DelegatingHandler que injeta Bearer token em toda chamada do AdminApiClient
/// e tenta refresh automaticamente quando a API responde 401. Adaptado do
/// TokenRefreshHandler do EasyStock.Web — mesmo padrão, sessão diferente.
/// </summary>
public class AdminTokenRefreshHandler(
    AdminSessionService session,
    ILogger<AdminTokenRefreshHandler> log,
    IHttpContextAccessor httpContextAccessor) : DelegatingHandler
{
    // DelegatingHandler do HttpClientFactory é singleton mesmo registrado como Transient —
    // a pool reusa a instância. Flag mutável de instância gera race entre requests
    // concorrentes do mesmo HttpClient. Serialize refresh via SemaphoreSlim por
    // refreshToken — janela curta evita stampede sem bloquear chamadas de outros usuários.
    private static readonly SemaphoreSlim _refreshGate = new(1, 1);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var isAuthRoute = request.RequestUri?.PathAndQuery.Contains("/auth/", StringComparison.OrdinalIgnoreCase) == true;
        var token = session.GetToken();
        HttpRequestMessage? retryRequest = null;

        if (!isAuthRoute && !string.IsNullOrEmpty(token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            retryRequest = await CloneRequestAsync(request, ct);
        }

        var response = await base.SendAsync(request, ct);

        if (response.StatusCode == HttpStatusCode.Unauthorized && !isAuthRoute)
        {
            var refreshToken = session.GetRefreshToken();
            if (string.IsNullOrEmpty(refreshToken))
            {
                MarkSessionExpired();
                session.ClearSession();
                return response;
            }

            await _refreshGate.WaitAsync(ct);
            try
            {
                // Outra request concorrente pode ter renovado o token enquanto estávamos esperando.
                var currentToken = session.GetToken();
                string? newToken = currentToken != token && !string.IsNullOrEmpty(currentToken)
                    ? currentToken
                    : await TryRefreshAsync(refreshToken, ct);

                if (newToken != null && retryRequest is not null)
                {
                    retryRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", newToken);
                    response.Dispose();
                    response = await base.SendAsync(retryRequest, ct);
                }
                else if (newToken == null)
                {
                    log.LogWarning("Token refresh falhou — limpando sessao admin");
                    MarkSessionExpired();
                    session.ClearSession();
                }
            }
            finally
            {
                _refreshGate.Release();
            }
        }

        return response;
    }

    /// <summary>
    /// Cookie efemero (_se_admin) sinaliza pro layout que a sessao expirou —
    /// permite mostrar "sua sessao expirou, faca login novamente" no Login
    /// sem precisar de query param.
    /// </summary>
    private void MarkSessionExpired()
    {
        try
        {
            var ctx = httpContextAccessor.HttpContext;
            if (ctx is null) return;
            ctx.Response.Cookies.Append(
                "_se_admin", "1",
                new CookieOptions
                {
                    HttpOnly = true,
                    Secure = ctx.Request.IsHttps,
                    Path = "/",
                    Expires = DateTimeOffset.UtcNow.AddSeconds(30),
                    SameSite = SameSiteMode.Strict
                });
        }
        catch
        {
            // Falha ao escrever cookie nao bloqueia o fluxo.
        }
    }

    private async Task<string?> TryRefreshAsync(string refreshToken, CancellationToken ct)
    {
        try
        {
            var body = JsonSerializer.Serialize(new { refreshToken });
            var content = new StringContent(body, Encoding.UTF8, "application/json");

            var refreshRequest = new HttpRequestMessage(HttpMethod.Post, "api/auth/refresh") { Content = content };
            var resp = await base.SendAsync(refreshRequest, ct);

            if (!resp.IsSuccessStatusCode) return null;

            var json = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);

            // Resposta pode vir como { data: { token, refreshToken } } ou { token, refreshToken }.
            var root = doc.RootElement;
            if (root.TryGetProperty("data", out var data))
                root = data;

            var newAccess = root.TryGetProperty("token", out var t) ? t.GetString() : null;
            var newRefresh = root.TryGetProperty("refreshToken", out var rt) ? rt.GetString() : null;

            if (!string.IsNullOrEmpty(newAccess))
            {
                session.SetTokens(newAccess, newRefresh ?? refreshToken);
                return newAccess;
            }

            return null;
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Excecao durante refresh de token");
            return null;
        }
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
