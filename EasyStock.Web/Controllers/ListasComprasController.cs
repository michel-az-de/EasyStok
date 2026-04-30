using EasyStock.Web.Models.ViewModels.ListasCompras;
using EasyStock.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace EasyStock.Web.Controllers;

public class ListasComprasController(ListasComprasService svc, SessionService session) : BaseController(session)
{
    [HttpGet("/listas-compras")]
    public async Task<IActionResult> Index(string? status = null)
    {
        ViewBag.Title = "Listas de compras";
        ViewBag.ActiveMenuItem = "ListasCompras";

        var vm = new ListasComprasIndexViewModel { FiltroStatus = status };
        var result = await svc.ListarAsync(status);
        if (result.Success && result.Data is not null) vm.Listas = result.Data;

        return View(vm);
    }

    [HttpGet("/listas-compras/{id}")]
    public async Task<IActionResult> Detail(string id)
    {
        ViewBag.Title = "Lista de compras";
        ViewBag.ActiveMenuItem = "ListasCompras";

        var result = await svc.ObterAsync(id);
        if (HasError(result) || result.Data is null) return RedirectToAction(nameof(Index));

        return View(new ListaComprasDetailViewModel { Detalhe = result.Data });
    }

    [HttpPost("/listas-compras")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Criar(string nome, string? observacoes)
    {
        var result = await svc.CriarAsync(nome, observacoes);
        if (HasError(result)) return RedirectToAction(nameof(Index));
        Toast("success", "Lista criada.");
        return RedirectToAction(nameof(Detail), new { id = result.Data?.Id });
    }

    [HttpPost("/listas-compras/{id}/arquivar")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Arquivar(string id)
    {
        var result = await svc.ArquivarAsync(id);
        if (HasError(result)) return RedirectToAction(nameof(Detail), new { id });
        Toast("success", "Lista arquivada.");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("/listas-compras/{id}/reabrir")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reabrir(string id)
    {
        var result = await svc.ReabrirAsync(id);
        if (HasError(result)) return RedirectToAction(nameof(Index));
        Toast("success", "Lista reaberta.");
        return RedirectToAction(nameof(Detail), new { id });
    }

    [HttpPost("/listas-compras/{id}/itens")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddItem(string id, string texto, decimal? quantidade, string? unidade, string? categoria)
    {
        var result = await svc.AddItemAsync(id, texto, quantidade, unidade, categoria, null);
        if (HasError(result)) return RedirectToAction(nameof(Detail), new { id });
        return RedirectToAction(nameof(Detail), new { id });
    }

    [HttpPost("/listas-compras/{id}/itens/{itemId}/toggle")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleItem(string id, string itemId, bool done)
    {
        var result = await svc.ToggleItemAsync(id, itemId, done);
        if (HasError(result)) return RedirectToAction(nameof(Detail), new { id });
        return RedirectToAction(nameof(Detail), new { id });
    }

    [HttpPost("/listas-compras/{id}/itens/{itemId}/excluir")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveItem(string id, string itemId)
    {
        var result = await svc.RemoveItemAsync(id, itemId);
        if (HasError(result)) return RedirectToAction(nameof(Detail), new { id });
        Toast("success", "Item removido.");
        return RedirectToAction(nameof(Detail), new { id });
    }
}
