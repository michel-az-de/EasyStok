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
            return RedirectToAction("Index", "Launcher");

        // Voltar para o login abandona qualquer selecao de empresa em curso — a senha
        // guardada para o passo 2 nao sobrevive a desistencia (ADR-0047).
        session.LimparLoginPendente();

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

        if (string.IsNullOrEmpty(token))
        {
            ModelState.AddModelError(string.Empty, "Resposta inválida do servidor. Tente novamente.");
            return View(vm);
        }

        // A Api emite token SEM empresaId quando o usuario tem 2+ empresas ativas — ela
        // nao tem como escolher por ele. Checar ANTES de gravar sessao/cookie: o token
        // ainda nao serve para nada, e antes o codigo autenticava para logo desfazer.
        var empresaId = jwt.TryReadClaim(token, "empresaId");
        if (string.IsNullOrEmpty(empresaId))
            return await IniciarSelecaoDeEmpresaAsync(vm, returnUrl);

        return await ConcluirLoginAsync(data, token, empresaId, vm.Email, vm.ManterLogado, returnUrl);
    }

    /// <summary>
    /// Passo 1 do login em duas etapas (ADR-0047). Sem <c>empresaId</c> no token, pergunta
    /// a Api quais empresas o usuario acessa. Duas ou mais: guarda a pendencia e manda pro
    /// seletor. Qualquer outro caso e anomalia (SuperAdmin sem empresa, vinculo faltando) e
    /// mantem a mensagem de suporte que ja existia.
    /// </summary>
    private async Task<IActionResult> IniciarSelecaoDeEmpresaAsync(LoginViewModel vm, string? returnUrl)
    {
        var empresasResult = await api.PostAsync<ListaEmpresasLoginApi>(
            "auth/lista-empresas", new { email = vm.Email, senha = vm.Senha });

        var empresas = empresasResult.Success ? empresasResult.Data?.Empresas ?? [] : [];
        if (empresas.Count < 2)
        {
            ModelState.AddModelError(string.Empty, "Não foi possível identificar a empresa associada a este usuário. Entre em contato com o suporte.");
            return View("Login", vm);
        }

        session.SetLoginPendente(new LoginPendente(
            vm.Email, vm.Senha, vm.ManterLogado, returnUrl,
            [.. empresas.Select(e => new EmpresaLoginItem(e.Id, e.Nome))],
            DateTime.UtcNow.Add(SessionService.LoginPendenteTtl)));

        return RedirectToAction(nameof(SelecionarEmpresa));
    }

    [AllowAnonymous]
    [HttpGet("/auth/selecionar-empresa")]
    public IActionResult SelecionarEmpresa()
    {
        var pendente = session.GetLoginPendente(DateTime.UtcNow);
        if (pendente is null) return RedirectToAction(nameof(Login));

        return View(new SelecionarEmpresaViewModel
        {
            Email = pendente.Email,
            Empresas = pendente.Empresas,
        });
    }

    [AllowAnonymous]
    [HttpPost("/auth/selecionar-empresa")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SelecionarEmpresa(string empresaId)
    {
        var pendente = session.GetLoginPendente(DateTime.UtcNow);
        if (pendente is null)
        {
            TempData["Toast"] = "warning|A seleção de empresa expirou. Entre novamente.";
            return RedirectToAction(nameof(Login));
        }

        // A senha sai da sessao ANTES da chamada — o passo 2 e de uso unico, tenha ele
        // sucesso ou nao. A Api revalida o vinculo com a empresa (sem IDOR); esta
        // checagem aqui evita gastar a ida.
        session.LimparLoginPendente();

        if (!pendente.Empresas.Any(e => e.Id == empresaId))
            return RedirectToAction(nameof(Login));

        var result = await api.PostAsync<JsonElement>(
            "auth/login", new { email = pendente.Email, senha = pendente.Senha, empresaId });

        var token = result.Success ? GetString(result.Data, "token") : null;
        if (string.IsNullOrEmpty(token))
        {
            TempData["Toast"] = "error|Não foi possível entrar nessa empresa. Tente novamente.";
            return RedirectToAction(nameof(Login));
        }

        var empresaDoToken = jwt.TryReadClaim(token, "empresaId");
        if (string.IsNullOrEmpty(empresaDoToken))
        {
            TempData["Toast"] = "error|Não foi possível entrar nessa empresa. Tente novamente.";
            return RedirectToAction(nameof(Login));
        }

        return await ConcluirLoginAsync(
            result.Data, token, empresaDoToken, pendente.Email, pendente.ManterLogado, pendente.ReturnUrl);
    }

    /// <summary>
    /// Pipeline pos-token, comum ao login direto e ao login em duas etapas: grava sessao,
    /// tema, cookie de autenticacao e resolve a loja de destino. Chamado apenas quando ja
    /// existe <paramref name="empresaId"/>.
    /// </summary>
    private async Task<IActionResult> ConcluirLoginAsync(
        JsonElement data, string token, string empresaId, string email, bool manterLogado, string? returnUrl)
    {
        var refreshToken = GetString(data, "refreshToken");
        session.SetTokens(token, refreshToken ?? string.Empty);
        session.SetEmpresaId(empresaId);

        var usuario = data.TryGetProperty("usuario", out var u) ? u : data;
        var nivel = GetString(usuario, "nivel") ?? GetString(usuario, "role") ?? "Operador";
        session.SetUsuario(
            GetString(usuario, "id") ?? string.Empty,
            GetString(usuario, "nome") ?? email,
            nivel
        );

        var meResult = await api.GetAsync<JsonElement>("auth/me");
        session.SetTemaPreferido(meResult.Success ? GetString(meResult.Data, "temaPreferido") : "light");

        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, GetString(usuario, "nome") ?? email),
            new(ClaimTypes.Email, email),
            new(ClaimTypes.Role, nivel),
            new("empresaId", empresaId),
        };

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var authProps = new AuthenticationProperties
        {
            IsPersistent = manterLogado,
            ExpiresUtc = manterLogado
                ? DateTimeOffset.UtcNow.AddDays(30)
                : DateTimeOffset.UtcNow.AddMinutes(480)
        };
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity), authProps);

        // Se "permanecer logado", persiste o refresh token num cookie HttpOnly para
        // sobreviver a deploys (DistributedMemoryCache é zerada a cada restart)
        if (manterLogado && !string.IsNullOrEmpty(refreshToken))
        {
            Response.Cookies.Append("_rt", refreshToken, new CookieOptions
            {
                HttpOnly = true,
                Secure = !env.IsDevelopment(),
                SameSite = SameSiteMode.Strict,
                Expires = DateTimeOffset.UtcNow.AddDays(30)
            });
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
        // cairia direto no Launcher sem LojaId, conseguindo navegar e tentar criar
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
        return RedirectToAction("Index", "Launcher");
    }

    [AllowAnonymous]
    [HttpGet("/auth/esqueci-senha")]
    public IActionResult EsqueciSenha()
    {
        if (session.IsLoggedIn())
            return RedirectToAction("Index", "Launcher");
        return View();
    }

    [AllowAnonymous]
    [HttpPost("/auth/esqueci-senha")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EsqueciSenha(ForgotPasswordViewModel vm)
    {
        if (!ModelState.IsValid) return View(vm);

        // N8: a base do link vem da configuracao da API (Auth:LinkRedefinirSenha), nunca do corpo; so o e-mail segue.
        await api.PostAsync<object>("auth/forgot-password", new { email = vm.Email });

        // Always show success to avoid revealing if email exists
        ViewBag.Sent = true;
        return View(new ForgotPasswordViewModel());
    }

    [AllowAnonymous]
    [HttpGet("/auth/redefinir-senha")]
    public IActionResult RedefinirSenha(string token)
    {
        SemReferrer();
        if (string.IsNullOrWhiteSpace(token))
            return RedirectToAction(nameof(EsqueciSenha));

        return View(new ResetPasswordViewModel { Token = token });
    }

    [AllowAnonymous]
    [HttpPost("/auth/redefinir-senha")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RedefinirSenha(ResetPasswordViewModel vm)
    {
        SemReferrer();
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

    /// <summary>
    /// Tela do convite de primeiro acesso (N9). Abrir o link NUNCA chama a API e nunca consome: so mostra o formulario, que
    /// lê o token da query ou do fragmento (<c>#t=</c>, que o servidor nem chega a receber). Quem consome e o POST.
    /// </summary>
    [AllowAnonymous]
    [HttpGet("/auth/convite")]
    public IActionResult Convite(string? token)
    {
        SemReferrer();
        return View(new AceitarConviteViewModel { Token = token ?? string.Empty });
    }

    [AllowAnonymous]
    [HttpPost("/auth/convite")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Convite(AceitarConviteViewModel vm)
    {
        SemReferrer();
        if (!ModelState.IsValid) return View(vm);

        var result = await api.PostAsync<object>("auth/aceitar-convite", new { token = vm.Token, novaSenha = vm.NovaSenha });
        if (!result.Success)
        {
            ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "Convite inválido ou expirado.");
            return View(vm);
        }

        TempData["Toast"] = "success|Senha criada! Entre com seu e-mail e a senha que você acabou de definir.";
        return RedirectToAction(nameof(Login));
    }

    /// <summary>Tela do codigo de 6 digitos recebido no WhatsApp (N8). Quem recebeu o link usa a tela do link.</summary>
    [AllowAnonymous]
    [HttpGet("/auth/redefinir-senha-codigo")]
    public IActionResult RedefinirSenhaCodigo()
    {
        SemReferrer();
        return View(new ResetPasswordCodeViewModel());
    }

    [AllowAnonymous]
    [HttpPost("/auth/redefinir-senha-codigo")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RedefinirSenhaCodigo(ResetPasswordCodeViewModel vm)
    {
        SemReferrer();
        if (!ModelState.IsValid) return View(vm);

        var result = await api.PostAsync<object>(
            "auth/reset-password-code", new { email = vm.Email, codigo = vm.Codigo, novaSenha = vm.NovaSenha });
        if (!result.Success)
        {
            ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "Código inválido ou expirado.");
            vm.Codigo = string.Empty;
            return View(vm);
        }

        TempData["Toast"] = "success|Senha redefinida com sucesso! Faça login com a nova senha.";
        return RedirectToAction(nameof(Login));
    }

    // O token do link e o codigo nao podem vazar pelo Referer para nenhum recurso externo carregado pela pagina (N8).
    private void SemReferrer() => Response.Headers["Referrer-Policy"] = "no-referrer";

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
            : RedirectToAction("Index", "Launcher");

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

    // ExtractClaim removido — consolidado em IJwtClaimsReader (TASK-EZ-WEB-005).
}
