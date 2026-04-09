using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace EasyStock.Web.Services;

public class TokenRefreshHandler(SessionService session, ILogger<TokenRefreshHandler> log) : DelegatingHandler
{
    private bool _isRefreshing;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var isAuthRoute = request.RequestUri?.PathAndQuery.Contains("/auth/", StringComparison.OrdinalIgnoreCase) == true;
        var token = session.GetToken();
        var lojaId = session.GetLojaId();

        // Inject Bearer token + X-Loja-ID for authenticated requests
        if (!isAuthRoute && !string.IsNullOrEmpty(token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            if (!string.IsNullOrEmpty(lojaId))
            {
                request.Headers.TryAddWithoutValidation("X-Loja-ID", lojaId);

                // Inject empresaId as query param from session (empresa context, not loja)
                var empresaId = session.GetEmpresaId();
                if (!string.IsNullOrEmpty(empresaId))
                {
                    var uri = request.RequestUri!;
                    var uriBuilder = new UriBuilder(uri);
                    var query = System.Web.HttpUtility.ParseQueryString(uriBuilder.Query);
                    if (string.IsNullOrEmpty(query["empresaId"]))
                    {
                        query["empresaId"] = empresaId;
                        uriBuilder.Query = query.ToString();
                        request.RequestUri = uriBuilder.Uri;
                    }
                }
            }
        }

        var response = await base.SendAsync(request, ct);

        // Attempt token refresh on 401
        if (response.StatusCode == HttpStatusCode.Unauthorized && !isAuthRoute && !_isRefreshing)
        {
            var refreshToken = session.GetRefreshToken();
            if (!string.IsNullOrEmpty(refreshToken))
            {
                _isRefreshing = true;
                try
                {
                    var newToken = await TryRefreshAsync(refreshToken, ct);
                    if (newToken != null)
                    {
                        // Retry original request with new token
                        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", newToken);
                        response.Dispose();
                        response = await base.SendAsync(request, ct);
                    }
                    else
                    {
                        log.LogWarning("Token refresh failed — clearing session");
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
                session.Clear();
            }
        }

        return response;
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
}
