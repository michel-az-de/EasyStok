using EasyStock.Web.Models.ViewModels.Clientes;
using EasyStock.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace EasyStock.Web.Controllers;

/// <summary>
/// Painel /clientes-mobile — gestor revisa clientes criados no app
/// (mobile_clients). Pode linkar a Cliente ERP existente OU promover
/// criando um Cliente ERP novo a partir dos dados do mobile.
/// </summary>
public class ClientesMobileController(ClientesService svc, SessionService session) : BaseController(session)
{
    [HttpGet("/clientes-mobile")]
    public async Task<IActionResult> Index(bool pendingOnly = false)
    {
        ViewBag.Title = "Clientes (mobile)";
        ViewBag.ActiveMenuItem = "ClientesMobile";

        var vm = new ClientesMobileViewModel { PendingOnly = pendingOnly };
        try
        {
            var mobile = await svc.ListarMobileAsync(pendingOnly);
            if (mobile.Success && mobile.Data is not null) vm.Items = mobile.Data;

            var erp = await svc.ListarAsync(status: "ativo");
            if (erp.Success && erp.Data is not null) vm.ErpClientes = erp.Data;
        }
        catch
        {
            Toast("error", "Não foi possível carregar os dados. Tente novamente.");
        }

        return View(vm);
    }

    /// <summary>Linka mobile_client a um Cliente ERP existente.</summary>
    [HttpPost("/clientes-mobile/{id}/link")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Link(string id, Guid? erpClienteId)
    {
        var result = await svc.LinkMobileAsync(id, erpClienteId);
        if (HasError(result)) return RedirectToAction(nameof(Index));

        Toast("success", erpClienteId.HasValue ? "Cliente linkado." : "Cliente promovido pro ERP.");
        return RedirectToAction(nameof(Index));
    }

    /// <summary>Promove mobile_client criando Cliente ERP novo (mesma rota com erpClienteId=null).</summary>
    [HttpPost("/clientes-mobile/{id}/promover")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Promover(string id)
    {
        var result = await svc.LinkMobileAsync(id, null);
        if (HasError(result)) return RedirectToAction(nameof(Index));

        Toast("success", "Cliente promovido pro ERP.");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("/clientes-mobile/{id}/unlink")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Unlink(string id)
    {
        var result = await svc.UnlinkMobileAsync(id);
        if (HasError(result)) return RedirectToAction(nameof(Index));

        Toast("success", "Link removido.");
        return RedirectToAction(nameof(Index));
    }
}
