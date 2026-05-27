using System.Security.Claims;
using System.Text.Json;
using EasyStock.Web.Models.Api;
using EasyStock.Web.Models.ViewModels.Auth;
using EasyStock.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EasyStock.Web.Controllers;

public class AuthController(
    ApiClient api,
    SessionService session,
    IWebHostEnvironment env,
    IJwtClaimsReader jwt) : Controller
{
    [AllowAnonymous]
    [HttpGet("/auth/login")]
    public IActionResult Login(string? returnUrl = null, string? bye = null)
    {
        if (session.IsLoggedIn())
            return RedirectToAction("Index", "Dashboard");

        // Verifica se a sessão expirou (sinalizado pelo TokenRefreshHandler via cookie _se)
        if (Request.Cookies.ContainsKey("_se"))
        {
            ViewBag.SessionExpired = true;
            Response.Cookies.Delete("_se");
        }
        else if (bye == "1")
        {
            // Logout intencional — mostra confirmação no toast/banner.
            // Expiração prevalece se ambos vierem juntos.
            ViewBag.LogoutSuccess = true;
        }

        ViewBag.ReturnUrl = returnUrl;
        return View();
    }

    [AllowAnonymous]
    [HttpPost("/auth/login")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel vm, string? returnUrl = null)
    {
        // Preserva returnUrl em qualquer re-render da view (validacao falhou,
        // api fora, credenciais erradas) — sem isso o deep link e perdido apos
        // o primeiro POST com erro.
        ViewBag.ReturnUrl = returnUrl;

        if (!ModelState.IsValid) return View(vm);

        var result = await api.PostAsync<JsonElement>("auth/login", new { email = vm.Email, senha = vm.Senha });
        if (!result.Success)
        {
            var errorMsg = ClassifyLoginError(result.ErrorMessage);
            ModelState.AddModelError(string.Empty, errorMsg);
            ViewBag.ApiUnavailable = IsApiUnavailableError(result.ErrorMessage);
            return View(vm);
        }

        var data = result.Data;
        var token = GetString(data, "token");
        var refreshToken = GetString(data, "refreshToken");

        if (string.IsNullOrEmpty(token))
        {
            ModelState.AddModelError(string.Empty, "Resposta inválida do servidor. Tente novamente.");
            return View(vm);
        }

        session.SetTokens(token, refreshToken ?? string.Empty);

        var empresaId = jwt.TryReadClaim(token, "empresaId");
        if (!string.IsNullOrEmpty(empresaId))
            session.SetEmpresaId(empresaId);

        var usuario = data.TryGetProperty("usuario", out var u) ? u : data;
        var nivel = GetString(usuario, "nivel") ?? GetString(usuario, "role") ?? "Operador";
        session.SetUsuario(
            GetString(usuario, "id") ?? string.Empty,
            GetString(usuario, "nome") ?? vm.Email,
            nivel
        );

        var meResult = await api.GetAsync<JsonElement>("auth/me");
        if (meResult.Success)
        {
            var meData = meResult.Data;
            var temaPreferido = GetString(meData, "temaPreferido");
            session.SetTemaPreferido(temaPreferido);
        }
        else
        {
            session.SetTemaPreferido("light");
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, GetString(usuario, "nome") ?? vm.Email),
            new(ClaimTypes.Email, vm.Email),
            new(ClaimTypes.Role, nivel)
        };
        if (!string.IsNullOrEmpty(empresaId))
            claims.Add(new Claim("empresaId", empresaId));

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var authProps = new AuthenticationProperties
        {
            IsPersistent = vm.ManterLogado,
            ExpiresUtc = vm.ManterLogado
                ? DateTimeOffset.UtcNow.AddDays(30)
                : DateTimeOffset.UtcNow.AddMinutes(480)
        };
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity), authProps);

        // Se "permanecer logado", persiste o refresh token num cookie HttpOnly para
        // sobreviver a deploys (DistributedMemoryCache é zerada a cada restart)
        if (vm.ManterLogado && !string.IsNullOrEmpty(refreshToken))
        {
            Response.Cookies.Append("_rt", refreshToken, new CookieOptions
            {
                HttpOnly = true,
                Secure = !env.IsDevelopment(),
                SameSite = SameSiteMode.Strict,
                Expires = DateTimeOffset.UtcNow.AddDays(30)
            });
        }

        if (string.IsNullOrEmpty(empresaId))
        {
            session.Clear();
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            ModelState.AddModelError(string.Empty, "Não foi possível identificar a empresa associada a este usuário. Entre em contato com o suporte.");
            return View(vm);
        }

        var lojasResult = await api.GetAsync<List<Loja>>("lojas");
        if (lojasResult.Success && lojasResult.Data is { Count: > 0 } lojas)
        {
            if (lojas.Count == 1)
            {
                session.SetLoja(lojas[0].Id, lojas[0].Nome, lojas[0].Emoji, lojas[0].EmpresaId);
                return SafeRedirect(returnUrl);
            }

            TempData["Lojas"] = JsonSerializer.Serialize(lojas);
            return RedirectToAction(nameof(SelecionarLoja));
        }

        // 0 lojas (ou falha ao listar): manda pro wizard/aviso. Sem isso o usuario
        // cairia direto no Dashboard sem LojaId, conseguindo navegar e tentar criar
        // recursos numa loja inexistente.
        return RedirectToAction(nameof(SelecionarLoja));
    }

    [Authorize]
    [HttpGet("/auth/selecionar-loja")]
    public async Task<IActionResult> SelecionarLoja()
    {
        if (!session.IsLoggedIn()) return RedirectToAction(nameof(Login));

        // Sempre buscar lojas frescas da API — chegar aqui via menu/redirect não passa
        // pelo Login, então TempData pode estar vazio e ainda assim haver lojas.
        var lojasJson = TempData["Lojas"] as string;
        List<Loja> lojas;
        if (!string.IsNullOrEmpty(lojasJson))
        {
            lojas = JsonSerializer.Deserialize<List<Loja>>(lojasJson) ?? [];
            TempData.Keep("Lojas");
        }
        else
        {
            var lojasResult = await api.GetAsync<List<Loja>>("lojas");
            lojas = lojasResult.Success ? lojasResult.Data ?? [] : [];
        }

        // SuperAdmin precisa enxergar o caminho para criar/vincular loja — não basta
        // mostrar "nenhuma loja" sem ação. Para Admin/Operador a fonte correta é o
        // gestor da empresa, mas o link para Logout segue disponível.
        var role = session.GetUsuarioRole() ?? string.Empty;
        ViewBag.PodeCriarLoja = role is "Admin" or "SuperAdmin";
        return View(lojas);
    }

    [Authorize]
    [HttpPost("/auth/selecionar-loja")]
    [ValidateAntiForgeryToken]
    public IActionResult SelecionarLoja(string lojaId, string lojaNome, string? lojaEmoji, string? empresaId)
    {
        session.SetLoja(lojaId, lojaNome, lojaEmoji, empresaId);
        return RedirectToAction("Index", "Dashboard");
    }

    /// <summary>
    /// Handoff de impersonation vindo do EasyStock.Admin. Recebe o JWT por POST
    /// (form body) — nunca por querystring — para que o token não vaze em logs
    /// de servidor, history do browser ou referrer headers.
    /// </summary>
    [AllowAnonymous]
    [HttpPost("/auth/impersonate")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Impersonate([FromForm] string token, [FromForm] string? refreshToken = null)
    {
        if (string.IsNullOrWhiteSpace(token))
            return RedirectToAction(nameof(Login));

        session.Clear();
        session.SetTokens(token, refreshToken ?? string.Empty);

        var nome = jwt.TryReadClaim(token, "nome") ?? jwt.TryReadClaim(token, ClaimTypes.Name) ?? "Operador";
        var email = jwt.TryReadClaim(token, "email") ?? "";
        var nivel = jwt.TryReadClaim(token, "nivel") ?? "Admin";
        var empresaId = jwt.TryReadClaim(token, "empresaId");
        var userId = jwt.TryReadClaim(token, "sub") ?? "";

        if (!string.IsNullOrEmpty(empresaId))
            session.SetEmpresaId(empresaId);
        session.SetUsuario(userId, nome, nivel);

        var lojas = await api.GetAsync<List<Loja>>("lojas");
        if (lojas.Success && lojas.Data is { Count: > 0 } ls)
            session.SetLoja(ls[0].Id, ls[0].Nome, ls[0].Emoji, ls[0].EmpresaId);

        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, nome),
            new(ClaimTypes.Email, email),
            new(ClaimTypes.Role, nivel)
        };
        if (!string.IsNullOrEmpty(empresaId))
            claims.Add(new Claim("empresaId", empresaId));

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var props = new AuthenticationProperties { IsPersistent = false, ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(60) };
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity), props);

        TempData["Toast"] = "info|Sessão de suporte iniciada. Saia ao terminar.";
        return RedirectToAction("Index", "Dashboard");
    }

    [AllowAnonymous]
    [HttpGet("/auth/registrar")]
    public IActionResult Registrar()
    {
        if (session.IsLoggedIn())
            return RedirectToAction("Index", "Dashboard");
        return View(new RegisterViewModel());
    }

    [AllowAnonymous]
    [HttpPost("/auth/registrar")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Registrar(RegisterViewModel vm)
    {
        if (!ModelState.IsValid) return View(vm);

        var result = await api.PostAsync<JsonElement>("empresas/registrar", new
        {
            nomeEmpresa = vm.NomeEmpresa,
            documento = vm.Documento,
            nomeAdmin = vm.NomeAdmin,
            emailAdmin = vm.Email,
            senhaAdmin = vm.Senha
        });

        if (!result.Success)
        {
            ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "Não foi possível criar a conta.");
            return View(vm);
        }

        var data = result.Data;
        var token = GetString(data, "token");
        var refreshToken = GetString(data, "refreshToken");

        if (string.IsNullOrEmpty(token))
        {
            TempData["Toast"] = "success|Conta criada! Faça login para continuar.";
            return RedirectToAction(nameof(Login));
        }

        session.SetTokens(token, refreshToken ?? string.Empty);

        var empresaId = jwt.TryReadClaim(token, "empresaId");
        if (!string.IsNullOrEmpty(empresaId))
            session.SetEmpresaId(empresaId);

        session.SetUsuario(
            GetString(data.TryGetProperty("usuario", out var u) ? u : data, "id") ?? string.Empty,
            vm.NomeAdmin,
            "Admin");

        session.SetTemaPreferido("light");

        // Após signup: se a empresa já tiver lojas (ex: signup repetido pelo mesmo
        // admin), usa a primeira. Senão, NÃO cria loja silenciosamente — o usuário
        // passa pelo wizard de onboarding obrigatório em /auth/selecionar-loja para
        // configurar nome/cidade/contato da primeira loja.
        var lojasResult = await api.GetAsync<List<Loja>>("lojas");
        var jaTemLoja = lojasResult.Success && lojasResult.Data is { Count: > 0 };
        if (jaTemLoja)
        {
            var lojas = lojasResult.Data!;
            session.SetLoja(lojas[0].Id, lojas[0].Nome, lojas[0].Emoji, lojas[0].EmpresaId);
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, vm.NomeAdmin),
            new(ClaimTypes.Email, vm.Email),
            new(ClaimTypes.Role, "Admin")
        };
        if (!string.IsNullOrEmpty(empresaId))
            claims.Add(new Claim("empresaId", empresaId));

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var authProps = new AuthenticationProperties
        {
            IsPersistent = false,
            ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(480)
        };
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity), authProps);

        if (!jaTemLoja)
        {
            TempData["Toast"] = "success|Conta criada! Vamos configurar sua loja em poucos passos.";
            return Redirect("/onboarding");
        }

        TempData["Toast"] = "success|Bem-vindo! Seu trial de 14 dias esta ativo.";
        return RedirectToAction("Index", "Dashboard");
    }

    [AllowAnonymous]
    [HttpGet("/auth/esqueci-senha")]
    public IActionResult EsqueciSenha()
    {
        if (session.IsLoggedIn())
            return RedirectToAction("Index", "Dashboard");
        return View();
    }

    [AllowAnonymous]
    [HttpPost("/auth/esqueci-senha")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EsqueciSenha(ForgotPasswordViewModel vm)
    {
        if (!ModelState.IsValid) return View(vm);

        var baseUrl = GetConfiguredPublicBaseUrl();
        if (baseUrl is null)
        {
            ModelState.AddModelError(string.Empty, "A URL pública da aplicação não está configurada corretamente.");
            return View(vm);
        }

        await api.PostAsync<object>("auth/forgot-password", new { email = vm.Email, baseUrl });

        // Always show success to avoid revealing if email exists
        ViewBag.Sent = true;
        return View(new ForgotPasswordViewModel());
    }

    [AllowAnonymous]
    [HttpGet("/auth/redefinir-senha")]
    public IActionResult RedefinirSenha(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return RedirectToAction(nameof(EsqueciSenha));

        return View(new ResetPasswordViewModel { Token = token });
    }

    [AllowAnonymous]
    [HttpPost("/auth/redefinir-senha")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RedefinirSenha(ResetPasswordViewModel vm)
    {
        if (!ModelState.IsValid) return View(vm);

        var result = await api.PostAsync<object>("auth/reset-password", new { token = vm.Token, novaSenha = vm.NovaSenha });
        if (!result.Success)
        {
            ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "Token inválido ou expirado.");
            return View(vm);
        }

        TempData["Toast"] = "success|Senha redefinida com sucesso! Faça login com a nova senha.";
        return RedirectToAction(nameof(Login));
    }

    [Authorize]
    [HttpPost("/auth/logout")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await api.PostAsync<object>("auth/logout", new { refreshToken = session.GetRefreshToken() ?? string.Empty });
        session.Clear();
        Response.Cookies.Delete("_rt");
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        // bye=1 sinaliza pro GET Login mostrar feedback de logout intencional
        // (toast verde + banner). Veja AuthController.Login (GET).
        return RedirectToAction(nameof(Login), new { bye = "1" });
    }

    [Authorize]
    [HttpPost("/auth/theme")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Theme([FromForm] string theme)
    {
        var normalizedTheme = string.Equals(theme, "dark", StringComparison.OrdinalIgnoreCase) ? "dark" : "light";
        var result = await api.PatchAsync<JsonElement>("auth/me", new { temaPreferido = normalizedTheme });
        if (!result.Success)
            return BadRequest(new { success = false, message = result.ErrorMessage ?? "Não foi possível salvar a preferência de tema." });

        session.SetTemaPreferido(normalizedTheme);
        return Json(new { success = true, theme = normalizedTheme });
    }

    private IActionResult SafeRedirect(string? returnUrl) =>
        !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl)
            ? Redirect(returnUrl)
            : RedirectToAction("Index", "Dashboard");

    private static string ClassifyLoginError(string? errorMessage)
    {
        if (string.IsNullOrEmpty(errorMessage))
            return "Credenciais inválidas. Verifique seu e-mail e senha.";

        if (IsApiUnavailableError(errorMessage))
            return "Serviço temporariamente indisponível. Tente novamente em alguns instantes.";

        // Mensagens de credenciais inválidas (mantém genérico por segurança)
        if (errorMessage.Contains("inválid", StringComparison.OrdinalIgnoreCase) ||
            errorMessage.Contains("invalid", StringComparison.OrdinalIgnoreCase) ||
            errorMessage.Contains("credenciais", StringComparison.OrdinalIgnoreCase) ||
            errorMessage.Contains("unauthorized", StringComparison.OrdinalIgnoreCase) ||
            errorMessage.Contains("senha", StringComparison.OrdinalIgnoreCase))
            return "E-mail ou senha incorretos. Verifique suas credenciais.";

        if (errorMessage.Contains("429") || errorMessage.Contains("muitas requisições", StringComparison.OrdinalIgnoreCase))
            return "Muitas tentativas de login. Aguarde alguns minutos e tente novamente.";

        return "Não foi possível realizar o login. Tente novamente.";
    }

    private static bool IsApiUnavailableError(string? errorMessage)
    {
        if (string.IsNullOrEmpty(errorMessage)) return false;
        return errorMessage.Contains("timeout", StringComparison.OrdinalIgnoreCase) ||
               errorMessage.Contains("connection", StringComparison.OrdinalIgnoreCase) ||
               errorMessage.Contains("unreachable", StringComparison.OrdinalIgnoreCase) ||
               errorMessage.Contains("não foi possível conectar", StringComparison.OrdinalIgnoreCase) ||
               errorMessage.Contains("TaskCanceledException", StringComparison.OrdinalIgnoreCase) ||
               errorMessage.Contains("HttpRequestException", StringComparison.OrdinalIgnoreCase);
    }

    private static string? GetString(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static string? GetConfiguredPublicBaseUrl()
    {
        var configuredBaseUrl = Environment.GetEnvironmentVariable("PUBLIC_BASE_URL");
        if (string.IsNullOrWhiteSpace(configuredBaseUrl))
            return null;

        if (!Uri.TryCreate(configuredBaseUrl, UriKind.Absolute, out var uri))
            return null;

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            return null;

        return uri.GetLeftPart(UriPartial.Authority);
    }

    // ExtractClaim removido — consolidado em IJwtClaimsReader (TASK-EZ-WEB-005).
}
