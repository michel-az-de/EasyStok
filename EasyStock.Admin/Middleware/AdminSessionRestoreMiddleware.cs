namespace EasyStock.Admin.Middleware;

/// <summary>
/// Restaura a sessao do admin a partir do cookie persistente _rt_admin (refresh
/// token) quando a sessao in-memory foi zerada por um deploy/restart. Espelha o
/// SessionRestoreMiddleware do EasyStock.Web, adaptado ao modelo de auth do Admin
/// (gate por token na sessao via AdminPageBase — sem cookie de claims).
///
/// Safe-by-construction: so age quando a sessao esta vazia E o _rt_admin existe; e
/// totalmente envolto em try/catch (TryRestoreAsync retorna false em qualquer falha).
/// Sucesso -> repopula a sessao e segue. Falha -> apaga o cookie, marca _se_admin e
/// segue; o AdminPageBase entao redireciona pro login, exatamente como hoje. Nunca
/// interfere numa sessao valida (so entra no branch de sessao vazia).
/// </summary>
public class AdminSessionRestoreMiddleware(
    RequestDelegate next,
    ILogger<AdminSessionRestoreMiddleware> log,
    IWebHostEnvironment env)
{
    private const string RememberCookie = "_rt_admin";

    public async Task InvokeAsync(HttpContext context, AdminSessionService session, AdminApiClient api)
    {
        var path = context.Request.Path.Value ?? "";
        var skip = path.StartsWith("/Auth/", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("/health", StringComparison.OrdinalIgnoreCase);

        if (!skip && string.IsNullOrEmpty(session.GetToken()))
        {
            var rt = context.Request.Cookies[RememberCookie];
            if (!string.IsNullOrEmpty(rt))
            {
                var outcome = await TryRestoreAsync(context, session, api, rt);
                if (outcome == RestoreOutcome.Invalid)
                {
                    // Token genuinamente rejeitado (401) — apaga o cookie e sinaliza expiracao.
                    context.Response.Cookies.Delete(RememberCookie);
                    MarkSessionExpired(context);
                }
                else if (outcome == RestoreOutcome.Transient)
                {
                    // API indisponivel (ex.: reiniciando no mesmo deploy). NAO apaga o cookie:
                    // a proxima request re-tenta e a sessao se auto-restaura quando a API volta.
                    // So sinaliza o banner de expiracao; o _rt_admin sobrevive (self-heal).
                    MarkSessionExpired(context);
                }
            }
        }

        await next(context);
    }

    // Resultado da restauracao. Invalid = token rejeitado (401 -> apaga o cookie);
    // Transient = API indisponivel (rede/timeout/5xx -> mantem o cookie e re-tenta depois).
    private enum RestoreOutcome { Restored, Invalid, Transient }

    private async Task<RestoreOutcome> TryRestoreAsync(HttpContext context, AdminSessionService session, AdminApiClient api, string refreshToken)
    {
        // NOVO-05: retry com backoff curto bridgia a janela em que a API reinicia junto
        // (mesmo deploy). So o 401 (token rejeitado) e definitivo; o resto e transitorio.
        const int maxAttempts = 3;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                var resp = await api.PostRawAsync("api/auth/refresh", new { refreshToken });

                var root = resp;
                if (resp.ValueKind == JsonValueKind.Object && resp.TryGetProperty("data", out var data))
                    root = data;

                var newToken = root.ValueKind == JsonValueKind.Object && root.TryGetProperty("token", out var t) ? t.GetString() : null;
                var newRefresh = root.ValueKind == JsonValueKind.Object && root.TryGetProperty("refreshToken", out var rt) ? rt.GetString() : null;

                if (string.IsNullOrEmpty(newToken))
                {
                    // Resposta sem token: tipicamente API com erro/reiniciando (envelope de erro
                    // ou 5xx). Transitorio -> re-tenta; nao destroi o cookie.
                    if (attempt < maxAttempts) { await Task.Delay(BackoffMs(attempt)); continue; }
                    log.LogWarning("Restauracao via _rt_admin: API sem token apos {Attempts} tentativas (transitorio).", maxAttempts);
                    return RestoreOutcome.Transient;
                }

                // Defesa em profundidade: so restaura sessao de SuperAdmin (mesma regra do
                // login e do AdminPageBase). Token sem nivel=SuperAdmin nao vira sessao.
                var (nivel, nome, email) = ReadClaims(newToken);
                if (!string.Equals(nivel, "SuperAdmin", StringComparison.OrdinalIgnoreCase))
                    return RestoreOutcome.Invalid;

                session.SetSession(newToken, newRefresh ?? refreshToken, nome ?? "Admin", email ?? "");

                if (!string.IsNullOrEmpty(newRefresh))
                    context.Response.Cookies.Append(RememberCookie, newRefresh, RememberCookieOptions());

                log.LogInformation("Sessao admin restaurada via _rt_admin (tentativa {Attempt}).", attempt);
                return RestoreOutcome.Restored;
            }
            catch (SessionExpiredException)
            {
                // 401: refresh token genuinamente rejeitado/expirado — definitivo, apaga o cookie.
                return RestoreOutcome.Invalid;
            }
            catch (Exception ex)
            {
                // Rede/timeout/5xx (API reiniciando no mesmo deploy): transitorio. Re-tenta; se
                // persistir, mantem o cookie para auto-restaurar numa request futura.
                if (attempt < maxAttempts) { await Task.Delay(BackoffMs(attempt)); continue; }
                log.LogWarning(ex, "Restauracao via _rt_admin falhou apos {Attempts} tentativas (transitorio).", maxAttempts);
                return RestoreOutcome.Transient;
            }
        }
        return RestoreOutcome.Transient;
    }

    // Backoff curto e crescente entre tentativas de refresh (300ms, 600ms).
    private static int BackoffMs(int attempt) => 300 * attempt;

    private CookieOptions RememberCookieOptions() => new()
    {
        HttpOnly = true,
        Secure = !env.IsDevelopment(),
        SameSite = SameSiteMode.Strict,
        Expires = DateTimeOffset.UtcNow.AddDays(30)
    };

    // Sinaliza pro Login que a sessao expirou (banner amarelo). Mesmo cookie usado
    // pelo AdminTokenRefreshHandler.
    private static void MarkSessionExpired(HttpContext context)
    {
        try
        {
            context.Response.Cookies.Append("_se_admin", "1", new CookieOptions
            {
                HttpOnly = true,
                Secure = context.Request.IsHttps,
                Path = "/",
                Expires = DateTimeOffset.UtcNow.AddSeconds(30),
                SameSite = SameSiteMode.Strict
            });
        }
        catch { /* best-effort */ }
    }

    // Decodifica o payload do JWT (base64url) sem validar assinatura — a API valida
    // quando o token e usado. Aqui so extraimos nivel/nome/email pra repopular a sessao.
    private static (string? nivel, string? nome, string? email) ReadClaims(string token)
    {
        try
        {
            var parts = token.Split('.');
            if (parts.Length < 2) return (null, null, null);
            using var doc = JsonDocument.Parse(Base64UrlDecode(parts[1]));
            var r = doc.RootElement;
            string? Get(string k) => r.TryGetProperty(k, out var v) ? v.GetString() : null;
            return (Get("nivel"), Get("nome"), Get("email") ?? Get("sub"));
        }
        catch
        {
            return (null, null, null);
        }
    }

    private static byte[] Base64UrlDecode(string s)
    {
        s = s.Replace('-', '+').Replace('_', '/');
        switch (s.Length % 4)
        {
            case 2: s += "=="; break;
            case 3: s += "="; break;
        }
        return Convert.FromBase64String(s);
    }
}
