using System.Text.Json;
using EasyStock.Web.Models.ViewModels.Notificacoes;
using EasyStock.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace EasyStock.Web.Controllers;

public class NotificacoesController(NotificacoesService svc, SessionService session) : BaseController(session)
{
    [HttpGet("/notificacoes")]
    public async Task<IActionResult> Index(bool? lida = null, string? tipo = null)
    {
        ViewBag.Title = "Notificações";
        ViewBag.ActiveMenuItem = "Notificacoes";

        var result = await svc.ListarAsync(lida, tipo);
        var vm = new NotificacoesViewModel();

        if (result.Success)
        {
            vm.Items = result.Data!;
            vm.NaoLidas = vm.Items.Count(n => !n.Lida);
        }

        return View(vm);
    }

    [HttpGet("/notificacoes/badge")]
    public async Task<IActionResult> Badge()
    {
        var result = await svc.BadgeAsync();
        if (!result.Success) return Json(new { count = 0 });

        if (result.Data is JsonElement el && el.TryGetProperty("count", out var c))
            return Json(new { count = c.GetInt32() });

        return Json(new { count = 0 });
    }

    [HttpPost("/notificacoes/{id}/lida")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarcarLida(string id)
    {
        await svc.MarcarLidaAsync(id);
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("/notificacoes/marcar-todas")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarcarTodas()
    {
        await svc.MarcarTodasLidasAsync();
        Toast("success", "Todas as notificações marcadas como lidas.");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("/notificacoes/{id}/excluir")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Excluir(string id)
    {
        await svc.RemoverAsync(id);
        return RedirectToAction(nameof(Index));
    }
}
