using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;

namespace EasyStock.Web.Services;

public class TokenRefreshHandler(
    SessionService session,
    ILogger<TokenRefreshHandler> log,
    IHttpContextAccessor httpContextAccessor) : DelegatingHandler
{
    private bool _isRefreshing;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
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
                request.RequestUri = AddQueryString(request.RequestUri, "empresaId", empresaId);
                retryRequest!.RequestUri = AddQueryString(retryRequest.RequestUri, "empresaId", empresaId);
            }
        }

        var response = await base.SendAsync(request, ct);

        if (response.StatusCode == HttpStatusCode.Unauthorized && !isAuthRoute && !_isRefreshing)
        {
            var refreshToken = session.GetRefreshToken();
            if (!string.IsNullOrEmpty(refreshToken))
            {
                _isRefreshing = true;
                try
                {
                    var newToken = await TryRefreshAsync(refreshToken, ct);
                    if (newToken != null && retryRequest is not null)
                    {
                        retryRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", newToken);
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
                finally
                {
                    _isRefreshing = false;
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

    private void MarkSessionExpired()
    {
        try
        {
            httpContextAccessor.HttpContext?.Response.Cookies.Append(
                "_se", "1",
                new CookieOptions
                {
                    HttpOnly = false,
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

    private async Task<string?> TryRefreshAsync(string refreshToken, CancellationToken ct)
    {
        try
        {
            var body = JsonSerializer.Serialize(new { refreshToken });
            var content = new StringContent(body, Encoding.UTF8, "application/json");

            // Use inner handler directly to avoid re-entering this handler
            var refreshRequest = new HttpRequestMessage(HttpMethod.Post, "auth/refresh") { Content = content };
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
                session.SetTokens(newAccess, newRefresh ?? refreshToken);
                return newAccess;
            }

            return null;
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Exception during token refresh");
            return null;
        }
    }

    private static Uri? AddQueryString(Uri? uri, string key, string value)
    {
        if (uri is null) return null;

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
