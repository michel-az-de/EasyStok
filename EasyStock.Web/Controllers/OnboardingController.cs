using System.Text.Json;
using EasyStock.Web.Models.ViewModels.Onboarding;
using EasyStock.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EasyStock.Web.Controllers;

[Authorize]
public class OnboardingController(ApiClient api, SessionService session) : Controller
{
    [HttpGet("/onboarding")]
    public async Task<IActionResult> Index()
    {
        if (!session.IsLoggedIn()) return Redirect("/auth/login");

        // Se ja completo, vai direto pro dashboard.
        var status = await api.GetAsync<JsonElement>("onboarding/status");
        if (status.Success && status.Data.TryGetProperty("data", out var d)
            && d.TryGetProperty("completo", out var c) && c.ValueKind == JsonValueKind.True)
        {
            return RedirectToAction("Index", "Dashboard");
        }

        return View(new OnboardingViewModel());
    }

    [HttpPost("/onboarding")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(OnboardingViewModel vm)
    {
        if (!ModelState.IsValid) return View(vm);

        var result = await api.PostAsync<JsonElement>("onboarding/completar", new
        {
            nomeFantasia = vm.NomeFantasia,
            telefone = vm.Telefone,
            segmento = vm.Segmento,
            lojaNome = vm.LojaNome,
            lojaEndereco = vm.LojaEndereco,
            lojaTelefone = vm.LojaTelefone,
        });

        if (!result.Success)
        {
            ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "Nao foi possivel completar o onboarding.");
            return View(vm);
        }

        TempData["Toast"] = "success|Tudo pronto! Bem-vindo ao EasyStok.";
        return RedirectToAction("Index", "Dashboard");
    }
}
