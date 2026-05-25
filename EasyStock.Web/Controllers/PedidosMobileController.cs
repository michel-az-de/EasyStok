using EasyStock.Web.Models.ViewModels.Pedidos;
using EasyStock.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace EasyStock.Web.Controllers;

/// <summary>
/// Painel /pedidos-mobile — gestor revisa pedidos criados no app
/// (mobile_orders). Pode linkar a Pedido ERP existente OU promover.
/// </summary>
public class PedidosMobileController(PedidosService svc, SessionService session) : BaseController(session)
{
    [HttpGet("/pedidos-mobile")]
    public async Task<IActionResult> Index(bool pendingOnly = false)
    {
        ViewBag.Title = "Pedidos (mobile)";
        ViewBag.ActiveMenuItem = "PedidosMobile";

        var vm = new PedidosMobileViewModel { PendingOnly = pendingOnly };

        var mobile = await svc.ListarMobileAsync(pendingOnly);
        if (mobile.Success && mobile.Data is not null) vm.Items = mobile.Data;

        var erp = await svc.ListarAsync();
        if (erp.Success && erp.Data is not null) vm.ErpPedidos = erp.Data;

        return View(vm);
    }

    [HttpPost("/pedidos-mobile/{id}/link")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Link(string id, Guid? erpPedidoId)
    {
        var result = await svc.LinkMobileAsync(id, erpPedidoId);
        if (HasError(result)) return RedirectToAction(nameof(Index));

        Toast("success", erpPedidoId.HasValue ? "Pedido linkado." : "Pedido promovido pro ERP.");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("/pedidos-mobile/{id}/promover")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Promover(string id)
    {
        var result = await svc.LinkMobileAsync(id, null);
        if (HasError(result)) return RedirectToAction(nameof(Index));

        Toast("success", "Pedido promovido pro ERP.");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("/pedidos-mobile/{id}/unlink")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Unlink(string id)
    {
        var result = await svc.UnlinkMobileAsync(id);
        if (HasError(result)) return RedirectToAction(nameof(Index));

        Toast("success", "Link removido.");
        return RedirectToAction(nameof(Index));
    }
}
