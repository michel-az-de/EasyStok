using EasyStock.Web.Models.ViewModels.Preferencias;
using EasyStock.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace EasyStock.Web.Controllers;

public class PreferenciasController(ApiClient api, SessionService session) : BaseController(session)
{
    [HttpGet("/preferencias")]
    public async Task<IActionResult> Index()
    {
        ViewBag.Title = "Preferências de Notificação";
        ViewBag.ActiveMenuItem = "Preferencias";

        var result = await api.GetAsync<List<ConsentimentoDto>>("api/consentimentos");
        var vm = new PreferenciasViewModel();

        if (result.Success && result.Data != null)
        {
            foreach (var c in result.Data)
                vm.SetOptIn(c.Canal, c.Categoria, c.OptIn);
        }

        return View(vm);
    }

    [HttpPost("/preferencias")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Salvar()
    {
        var canais = PreferenciasViewModel.Canais;
        var categorias = PreferenciasViewModel.Categorias;
        var erros = new List<string>();

        foreach (var canal in canais)
        {
            foreach (var categoria in categorias)
            {
                if (categoria == "Transacional") continue; // sempre opt-in, não modificar

                var key = $"toggle_{canal}_{categoria}";
                var optIn = Request.Form.ContainsKey(key);
                var endpoint = optIn ? "api/consentimentos/opt-in" : "api/consentimentos/opt-out";
                var result = await api.PostAsync<object>(endpoint, new { canal, categoria });
                if (!result.Success)
                    erros.Add($"{canal}/{categoria}: {result.ErrorMessage}");
            }
        }

        if (erros.Count > 0)
            Toast("error", $"Algumas preferências não foram salvas: {string.Join("; ", erros)}");
        else
            Toast("success", "Preferências de notificação atualizadas com sucesso.");

        return RedirectToAction(nameof(Index));
    }
}
