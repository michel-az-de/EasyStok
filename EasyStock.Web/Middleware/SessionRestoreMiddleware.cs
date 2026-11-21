using System.Collections.Concurrent;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
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
public class SessionRestoreMiddleware(RequestDelegate next, ILogger<SessionRestoreMiddleware> log, IWebHostEnvironment env)
{
    private const string RememberCookie = "_rt";

    /// <summary>
    /// #2745: a Api consome o refresh uma vez só. Restaurações paralelas do mesmo _rt (abas e fragmentos depois
    /// de um deploy) compartilham uma renovação, como no TokenRefreshHandler; a entrada vive 30 s para quem
    /// chegar logo depois ainda com o cookie antigo. A chave é o usuário mais o hash do _rt, nunca o segredo; a
    /// falha transitória (queda, timeout, 429, 5xx) sai na hora, para a próxima requisição tentar de novo.
    /// </summary>
    private static readonly ConcurrentDictionary<string, Lazy<Task<ApiResult<JsonElement>>>> Renovacoes = new();
    private static readonly TimeSpan Retencao = TimeSpan.FromSeconds(30);

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
                    var restauracao = await TryRestoreAsync(context, session, api, jwt, rt);
                    if (restauracao == Restauracao.Recusada)
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

    private enum Restauracao { Restaurada, Recusada, Indisponivel }

    private async Task<Restauracao> TryRestoreAsync(HttpContext context, SessionService session, ApiClient api, IJwtClaimsReader jwt, string refreshToken)
    {
        try
        {
            var result = await RenovarUmaVezAsync(api, ChaveDoVoo(context.User, refreshToken), refreshToken);
            if (!result.Success && EhTransitoria(result))
            {
                // A Api caiu ou demorou: o _rt pode seguir válido; não desloga, a próxima requisição tenta de novo.
                log.LogWarning("Restauração de sessão adiada: Api indisponível (HTTP {Status})", result.HttpStatus);
                return Restauracao.Indisponivel;
            }
            if (!result.Success || result.Data.ValueKind == JsonValueKind.Undefined)
            {
                log.LogWarning("Restauração de sessão falhou: refresh token inválido ou expirado");
                return Restauracao.Recusada;
            }

            var newToken = GetString(result.Data, "token");
            var newRefresh = GetString(result.Data, "refreshToken");
            if (string.IsNullOrEmpty(newToken))
            {
                log.LogWarning("Restauração de sessão falhou: resposta sem token");
                return Restauracao.Recusada;
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
                    // Issue 808: alinhado ao login (Secure = !IsDevelopment). Fixo em true,
                    // em dev HTTP o browser descartava o cookie rotacionado e o remember-me
                    // quebrava silenciosamente.
                    Secure = !env.IsDevelopment(),
                    SameSite = SameSiteMode.Strict,
                    Expires = DateTimeOffset.UtcNow.AddDays(30)
                });
            }

            log.LogInformation("Sessão restaurada via remember-me para usuário {UserId}", userId);
            return Restauracao.Restaurada;
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Erro ao restaurar sessão via remember-me");
            return Restauracao.Recusada;
        }
    }

    private static bool EhTransitoria(ApiResult<JsonElement> result) =>
        result.HttpStatus is 0 or 408 or 429 or >= 500;

    private static string ChaveDoVoo(ClaimsPrincipal usuario, string refreshToken) =>
        $"{usuario.FindFirstValue(ClaimTypes.Email)}:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(refreshToken)))}";

    private static Task<ApiResult<JsonElement>> RenovarUmaVezAsync(ApiClient api, string chave, string refreshToken)
    {
        var voo = new Lazy<Task<ApiResult<JsonElement>>>(
            () => api.PostAsync<JsonElement>("auth/refresh", new { refreshToken }));
        var atual = Renovacoes.GetOrAdd(chave, voo);
        if (ReferenceEquals(atual, voo))
            _ = LiberarAsync(chave, voo);
        return atual.Value;
    }

    private static async Task LiberarAsync(string chave, Lazy<Task<ApiResult<JsonElement>>> voo)
    {
        var reter = false;
        try { var result = await voo.Value; reter = result.Success || !EhTransitoria(result); }
        catch { /* a falha é tratada por quem espera o voo; sem retenção, a próxima tenta de novo */ }
        if (reter)
            await Task.Delay(Retencao);
        Renovacoes.TryRemove(new KeyValuePair<string, Lazy<Task<ApiResult<JsonElement>>>>(chave, voo));
    }

    private static string? GetString(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    // ExtractClaim removido — consolidado em IJwtClaimsReader (TASK-EZ-WEB-005).
}
