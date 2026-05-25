using EasyStock.Web.Models.ViewModels.Caixa;
using EasyStock.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace EasyStock.Web.Controllers;

/// <summary>Painel /caixa-mobile — revisão de cash entries do app.</summary>
public class CaixaMobileController(CaixaService svc, SessionService session) : BaseController(session)
{
    [HttpGet("/caixa-mobile")]
    public async Task<IActionResult> Index(bool pendingOnly = false)
    {
        ViewBag.Title = "Caixa (mobile)";
        ViewBag.ActiveMenuItem = "CaixaMobile";

        var vm = new CaixaMobileViewModel { PendingOnly = pendingOnly };
        var result = await svc.ListarMobileAsync(pendingOnly);
        if (result.Success && result.Data is not null) vm.Items = result.Data;
        return View(vm);
    }

    [HttpPost("/caixa-mobile/{id}/promover")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Promover(string id)
    {
        var result = await svc.PromoverMobileAsync(id);
        if (HasError(result)) return RedirectToAction(nameof(Index));
        Toast("success", "Lançamento promovido pro ERP.");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("/caixa-mobile/{id}/unlink")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Unlink(string id)
    {
        var result = await svc.UnlinkMobileAsync(id);
        if (HasError(result)) return RedirectToAction(nameof(Index));
        Toast("success", "Link removido.");
        return RedirectToAction(nameof(Index));
    }
}
