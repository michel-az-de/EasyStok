using System.Text.Json;
using EasyStock.Web.Models.ViewModels.Notificacoes;
using EasyStock.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace EasyStock.Web.Controllers;

public class NotificacoesController(NotificacoesService svc, SessionService session) : BaseController(session)
{
    [HttpGet("/notificacoes")]
    public async Task<IActionResult> Index(bool? lida = null, string? tipo = null, string? severidade = null)
    {
        ViewBag.Title = "Notificacoes";
        ViewBag.ActiveMenuItem = "Notificacoes";

        var resultTask = svc.ListarAsync(lida, tipo, severidade);
        var resumoTask = svc.ResumoAsync();

        await Task.WhenAll(resultTask, resumoTask);

        var result = await resultTask;
        var resumo = await resumoTask;

        var vm = new NotificacoesViewModel
        {
            FiltroTipo = tipo,
            FiltroSeveridade = severidade
        };

        if (result.Success)
        {
            vm.Items = result.Data!.Data;
            vm.NaoLidas = vm.Items.Count(n => !n.Lida);
        }

        if (resumo.Success)
            vm.Resumo = resumo.Data;

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

    [HttpGet("/notificacoes/recentes")]
    public async Task<IActionResult> Recentes()
    {
        var result = await svc.RecentesAsync();
        if (!result.Success) return Json(Array.Empty<object>());
        return Json(result.Data ?? []);
    }

    [HttpGet("/notificacoes/resumo")]
    public async Task<IActionResult> Resumo()
    {
        var result = await svc.ResumoAsync();
        if (!result.Success) return Json(new { totalNaoLidas = 0 });
        return Json(result.Data);
    }

    [HttpPost("/notificacoes/{id}/lida")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarcarLida(string id)
    {
        await svc.MarcarLidaAsync(id);
        Toast("success", "Notificação marcada como lida.");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("/notificacoes/{id}/lida-ajax")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarcarLidaAjax(string id)
    {
        var result = await svc.MarcarLidaAsync(id);
        return Json(new { success = result.Success });
    }

    [HttpPost("/notificacoes/marcar-todas")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarcarTodas()
    {
        await svc.MarcarTodasLidasAsync();
        Toast("success", "Todas as notificacoes marcadas como lidas.");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("/notificacoes/{id}/excluir")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Excluir(string id)
    {
        await svc.RemoverAsync(id);
        Toast("success", "Notificação removida.");
        return RedirectToAction(nameof(Index));
    }
}
