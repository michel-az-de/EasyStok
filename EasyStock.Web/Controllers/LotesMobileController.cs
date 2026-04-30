using EasyStock.Web.Models.ViewModels.Lotes;
using EasyStock.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace EasyStock.Web.Controllers;

public class LotesMobileController(LotesService svc, SessionService session) : BaseController(session)
{
    [HttpGet("/lotes-mobile")]
    public async Task<IActionResult> Index(bool pendingOnly = false)
    {
        ViewBag.Title = "Lotes (mobile)";
        ViewBag.ActiveMenuItem = "LotesMobile";

        var vm = new LotesMobileViewModel { PendingOnly = pendingOnly };
        var mobile = await svc.ListarMobileAsync(pendingOnly);
        if (mobile.Success && mobile.Data is not null) vm.Items = mobile.Data;

        var erp = await svc.ListarAsync();
        if (erp.Success && erp.Data is not null) vm.ErpLotes = erp.Data;

        return View(vm);
    }

    [HttpPost("/lotes-mobile/{id}/link")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Link(string id, Guid? erpLoteId)
    {
        var result = await svc.LinkMobileAsync(id, erpLoteId);
        if (HasError(result)) return RedirectToAction(nameof(Index));
        Toast("success", erpLoteId.HasValue ? "Lote linkado." : "Lote promovido pro ERP.");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("/lotes-mobile/{id}/promover")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Promover(string id)
    {
        var result = await svc.LinkMobileAsync(id, null);
        if (HasError(result)) return RedirectToAction(nameof(Index));
        Toast("success", "Lote promovido pro ERP.");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("/lotes-mobile/{id}/unlink")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Unlink(string id)
    {
        var result = await svc.UnlinkMobileAsync(id);
        if (HasError(result)) return RedirectToAction(nameof(Index));
        Toast("success", "Link removido.");
        return RedirectToAction(nameof(Index));
    }
}
