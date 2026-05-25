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

        var result = await api.GetAsync<List<ConsentimentoDto>>("consentimentos");
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

        var tarefas = from canal in canais
                      from categoria in categorias
                      where categoria != "Transacional" // sempre opt-in, não modificar
                      let key = $"toggle_{canal}_{categoria}"
                      let optIn = Request.Form.ContainsKey(key)
                      let endpoint = optIn ? "consentimentos/opt-in" : "consentimentos/opt-out"
                      select api.PostAsync<object>(endpoint, new { canal, categoria })
                          .ContinueWith(t =>
                          {
                              if (t.IsFaulted)
                                  lock (erros) erros.Add($"{canal}/{categoria}: erro de comunicação");
                              else if (t.IsCompletedSuccessfully && !t.Result.Success)
                                  lock (erros) erros.Add($"{canal}/{categoria}: {t.Result.ErrorMessage}");
                          }, TaskContinuationOptions.ExecuteSynchronously);

        await Task.WhenAll(tarefas);

        if (erros.Count > 0)
            Toast("error", $"Algumas preferências não foram salvas: {string.Join("; ", erros)}");
        else
            Toast("success", "Preferências de notificação atualizadas com sucesso.");

        return RedirectToAction(nameof(Index));
    }
}
