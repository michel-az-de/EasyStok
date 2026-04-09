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

        // Extract user info — API returns "nivel" (not "role")
        var usuario = data.TryGetProperty("usuario", out var u) ? u : data;
        var nivel = GetString(usuario, "nivel") ?? "Operador";
        session.SetUsuario(
            GetString(usuario, "id") ?? string.Empty,
            GetString(usuario, "nome") ?? vm.Email,
            nivel
        );

        // Sign in with cookie
        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, GetString(usuario, "nome") ?? vm.Email),
            new(ClaimTypes.Email, vm.Email),
            new(ClaimTypes.Role, nivel)
        };
        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));

        // Load lojas
        var lojasResult = await api.GetAsync<List<Loja>>("lojas");
        if (lojasResult.Success && lojasResult.Data is { Count: > 0 } lojas)
        {
            if (lojas.Count == 1)
            {
                if (!string.IsNullOrEmpty(lojas[0].EmpresaId))
                    session.SetEmpresaId(lojas[0].EmpresaId!);
                session.SetLoja(lojas[0].Id, lojas[0].Nome, lojas[0].Emoji);
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
        if (!string.IsNullOrEmpty(empresaId))
            session.SetEmpresaId(empresaId);
        session.SetLoja(lojaId, lojaNome, lojaEmoji);
        return RedirectToAction("Index", "Dashboard");
    }

    [HttpPost("/auth/logout")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await api.PostAsync<object>("auth/logout", new { });
        session.Clear();
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction(nameof(Login));
    }

    private static string? GetString(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
