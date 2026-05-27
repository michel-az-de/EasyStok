using System.Text.Json;
using EasyStock.Web.Models.Api;
using EasyStock.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace EasyStock.Web.Middleware;

/// <summary>
/// Restaura a sessão do usuário a partir do cookie _rt (refresh token persistente)
/// quando a sessão em memória foi zerada por um deploy ou restart do servidor.
/// Isso garante que "permanecer logado" sobreviva a deploys.
/// </summary>
public class SessionRestoreMiddleware(RequestDelegate next, ILogger<SessionRestoreMiddleware> log)
{
    private const string RememberCookie = "_rt";

    public async Task InvokeAsync(HttpContext context, SessionService session, ApiClient api, IJwtClaimsReader jwt)
    {
        var path = context.Request.Path.Value ?? "";

        // Não interferir em rotas de auth nem em erros
        if (!path.StartsWith("/auth/") && !path.StartsWith("/error/"))
        {
            var isAuthenticated = context.User.Identity?.IsAuthenticated == true;
            var sessionEmpty = string.IsNullOrEmpty(session.GetToken());

            if (isAuthenticated && sessionEmpty)
            {
                var rt = context.Request.Cookies[RememberCookie];
                if (!string.IsNullOrEmpty(rt))
                {
                    var restored = await TryRestoreAsync(context, session, api, jwt, rt);
                    if (!restored)
                    {
                        // Refresh token inválido/expirado — limpa tudo e força login
                        context.Response.Cookies.Delete(RememberCookie);
                        await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                        session.Clear();
                    }
                }
            }
        }

        await next(context);
    }

    private async Task<bool> TryRestoreAsync(HttpContext context, SessionService session, ApiClient api, IJwtClaimsReader jwt, string refreshToken)
    {
        try
        {
            var result = await api.PostAsync<JsonElement>("auth/refresh", new { refreshToken });
            if (!result.Success || result.Data.ValueKind == JsonValueKind.Undefined)
            {
                log.LogWarning("Restauração de sessão falhou: refresh token inválido ou expirado");
                return false;
            }

            var newToken = GetString(result.Data, "token");
            var newRefresh = GetString(result.Data, "refreshToken");
            if (string.IsNullOrEmpty(newToken))
            {
                log.LogWarning("Restauração de sessão falhou: resposta sem token");
                return false;
            }

            // Restaura tokens na sessão
            session.SetTokens(newToken, newRefresh ?? refreshToken);

            // Extrai dados do usuário do JWT
            var userId = jwt.TryReadClaim(newToken, "sub");
            var nome = jwt.TryReadClaim(newToken, "nome");
            var nivel = jwt.TryReadClaim(newToken, "nivel");
            var empresaId = jwt.TryReadClaim(newToken, "empresaId");
            session.SetUsuario(userId ?? "", nome ?? "Usuário", nivel ?? "Operador");
            if (!string.IsNullOrEmpty(empresaId))
                session.SetEmpresaId(empresaId);

            // Tenta restaurar loja (auto-seleciona se houver apenas uma)
            var lojasResult = await api.GetAsync<List<Loja>>("lojas");
            if (lojasResult.Success && lojasResult.Data is { Count: 1 } lojas)
                session.SetLoja(lojas[0].Id, lojas[0].Nome, lojas[0].Emoji, lojas[0].EmpresaId);

            // Restaura tema preferido
            var meResult = await api.GetAsync<JsonElement>("auth/me");
            if (meResult.Success && meResult.Data.ValueKind != JsonValueKind.Undefined)
                session.SetTemaPreferido(GetString(meResult.Data, "temaPreferido"));

            // Rotaciona o _rt cookie com o novo refresh token
            if (!string.IsNullOrEmpty(newRefresh))
            {
                context.Response.Cookies.Append(RememberCookie, newRefresh, new CookieOptions
                {
                    HttpOnly = true,
                    Secure = true,
                    SameSite = SameSiteMode.Strict,
                    Expires = DateTimeOffset.UtcNow.AddDays(30)
                });
            }

            log.LogInformation("Sessão restaurada via remember-me para usuário {UserId}", userId);
            return true;
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Erro ao restaurar sessão via remember-me");
            return false;
        }
    }

    private static string? GetString(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    // ExtractClaim removido — consolidado em IJwtClaimsReader (TASK-EZ-WEB-005).
}
