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

[AllowAnonymous]
public class AuthController(ApiClient api, SessionService session) : Controller
{
    [HttpGet("/auth/login")]
    public IActionResult Login(string? returnUrl = null)
    {
        if (session.IsLoggedIn())
            return RedirectToAction("Index", "Dashboard");
        ViewBag.ReturnUrl = returnUrl;
        return View();
    }

    [HttpPost("/auth/login")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel vm, string? returnUrl = null)
    {
        if (!ModelState.IsValid) return View(vm);

        var result = await api.PostAsync<JsonElement>("auth/login", new { email = vm.Email, senha = vm.Senha });
        if (!result.Success)
        {
            ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "Credenciais inválidas.");
            return View(vm);
        }

        var data = result.Data;
        var token = GetString(data, "token");
        var refreshToken = GetString(data, "refreshToken");

        if (string.IsNullOrEmpty(token))
        {
            ModelState.AddModelError(string.Empty, "Resposta inválida do servidor.");
            return View(vm);
        }

        session.SetTokens(token, refreshToken ?? string.Empty);

        var empresaId = ExtractClaim(token, "empresaId");
        if (!string.IsNullOrEmpty(empresaId))
            session.SetEmpresaId(empresaId);

        var usuario = data.TryGetProperty("usuario", out var u) ? u : data;
        var nivel = GetString(usuario, "nivel") ?? GetString(usuario, "role") ?? "Operador";
        session.SetUsuario(
            GetString(usuario, "id") ?? string.Empty,
            GetString(usuario, "nome") ?? vm.Email,
            nivel
        );

        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, GetString(usuario, "nome") ?? vm.Email),
            new(ClaimTypes.Email, vm.Email),
            new(ClaimTypes.Role, nivel)
        };
        if (!string.IsNullOrEmpty(empresaId))
            claims.Add(new Claim("empresaId", empresaId));

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));

        if (string.IsNullOrEmpty(empresaId))
        {
            session.Clear();
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            ModelState.AddModelError(string.Empty, "Nao foi possivel identificar a empresa deste usuario. Se houver mais de uma empresa vinculada, o login precisa ser ajustado antes de continuar.");
            return View(vm);
        }

        var lojasResult = await api.GetAsync<List<Loja>>("lojas");
        if (lojasResult.Success && lojasResult.Data is { Count: > 0 } lojas)
        {
            if (lojas.Count == 1)
            {
                session.SetLoja(lojas[0].Id, lojas[0].Nome, lojas[0].Emoji, lojas[0].EmpresaId);
                return Redirect(returnUrl ?? "/dashboard");
            }

            TempData["Lojas"] = JsonSerializer.Serialize(lojas);
            return RedirectToAction(nameof(SelecionarLoja));
        }

        return Redirect(returnUrl ?? "/dashboard");
    }

    [HttpGet("/auth/selecionar-loja")]
    public IActionResult SelecionarLoja()
    {
        if (!session.IsLoggedIn()) return RedirectToAction(nameof(Login));

        var lojasJson = TempData["Lojas"] as string;
        var lojas = string.IsNullOrEmpty(lojasJson)
            ? new List<Loja>()
            : JsonSerializer.Deserialize<List<Loja>>(lojasJson) ?? [];

        // Keep for the POST
        TempData.Keep("Lojas");
        return View(lojas);
    }

    [HttpPost("/auth/selecionar-loja")]
    [ValidateAntiForgeryToken]
    public IActionResult SelecionarLoja(string lojaId, string lojaNome, string? lojaEmoji, string? empresaId)
    {
        session.SetLoja(lojaId, lojaNome, lojaEmoji, empresaId);
        return RedirectToAction("Index", "Dashboard");
    }

    [HttpGet("/auth/registrar")]
    public IActionResult Registrar()
    {
        if (session.IsLoggedIn())
            return RedirectToAction("Index", "Dashboard");
        return View(new RegisterViewModel());
    }

    [HttpPost("/auth/registrar")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Registrar(RegisterViewModel vm)
    {
        if (!ModelState.IsValid) return View(vm);

        var result = await api.PostAsync<object>("empresas/registrar", new
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

        TempData["Toast"] = "success|Conta criada com sucesso! Faça login para continuar.";
        return RedirectToAction(nameof(Login));
    }

    [HttpGet("/auth/esqueci-senha")]
    public IActionResult EsqueciSenha()
    {
        if (session.IsLoggedIn())
            return RedirectToAction("Index", "Dashboard");
        return View();
    }

    [HttpPost("/auth/esqueci-senha")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EsqueciSenha(ForgotPasswordViewModel vm)
    {
        if (!ModelState.IsValid) return View(vm);

        await api.PostAsync<object>("auth/forgot-password", new { email = vm.Email });

        // Always show success to avoid revealing if email exists
        ViewBag.Sent = true;
        return View(new ForgotPasswordViewModel());
    }

    [HttpGet("/auth/redefinir-senha")]
    public IActionResult RedefinirSenha(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return RedirectToAction(nameof(EsqueciSenha));

        return View(new ResetPasswordViewModel { Token = token });
    }

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

    [HttpPost("/auth/logout")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await api.PostAsync<object>("auth/logout", new { refreshToken = session.GetRefreshToken() ?? string.Empty });
        session.Clear();
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction(nameof(Login));
    }

    private static string? GetString(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static string? ExtractClaim(string token, string claimType)
    {
        var parts = token.Split('.');
        if (parts.Length < 2) return null;

        var payload = parts[1];
        switch (payload.Length % 4)
        {
            case 2: payload += "=="; break;
            case 3: payload += "="; break;
        }

        payload = payload.Replace('-', '+').Replace('_', '/');

        try
        {
            var bytes = Convert.FromBase64String(payload);
            using var doc = JsonDocument.Parse(bytes);
            return doc.RootElement.TryGetProperty(claimType, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        }
        catch
        {
            return null;
        }
    }
}
